using System.Diagnostics;
using System.Runtime.InteropServices;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Dreamy.Missions;
using Newtonsoft.Json;

// Compiles the actual model and dependency sources, without Unity or model stubs.
// This measures .NET managed allocations, not Mono/IL2CPP device allocations or real disk IO.
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), warmup = 3, repeats = 3,
    serializer = typeof(JsonConvert).Assembly.FullName
}));
for (int i = 0; i < 3; i++) Measure(64, 30, 1, true, false, i, false);
foreach (bool serialize in new[] { false, true })
foreach (int receipts in new[] { 4096, 50000 })
foreach (int shots in new[] { 30, 60, 100 })
foreach (int perShot in new[] { 1, 3 })
foreach (bool batch in new[] { false, true })
for (int repeat = 0; repeat < 3; repeat++) Measure(receipts, shots, perShot, batch, serialize, repeat, true);

static void Measure(int receipts, int shots, int perShot, bool batch, bool serialize, int repeat, bool output)
{
    var store = new Store(serialize);
    MissionModel model = Create(store, receipts + 1000);
    store.Data.ActiveWindow.ProcessedEventIds.AddRange(Enumerable.Range(0, receipts).Select(i => "seed-" + i));
    var bundle = new ResolvedRewardBundle(1, "benchmark", new[] { new ResolvedRewardEntry("coin", "currency", "currency.coin", 1) });
    // Existing reward history must survive every progress transaction. Keep old windows small so the
    // active receipt journal dominates the fixture, rather than accidentally repeating it in every claim.
    for (int i = 0; i < 8; i++) store.Data.PendingClaims.Add(new MissionPendingClaim
    {
        CatalogId = "old", MissionId = "old", Bundle = bundle,
        WindowSnapshot = new MissionWindowData
        {
            Id = "old-" + i, StartUtcTicks = i, EndUtcTicks = i + 1, Revision = "old-revision",
            Period = new MissionPeriodPolicy(), Selection = new MissionSelectionPolicy { Count = 1 },
            Missions = new() { ["old"] = new MissionProgressData { Claimed = true, Progress = 256,
                DistinctValues = Enumerable.Range(0, 256).Select(n => "old-value-" + n).ToList() } },
            ProcessedEventIds = Enumerable.Range(0, 16).Select(n => "old-event-" + n).ToList()
        },
        Journal = new RewardJournal
        {
            RequestId = "claim-" + i, RequestFingerprint = "claim-fingerprint", State = RewardJournalState.Completed,
            BundleSchemaVersion = 1, ResolverVersion = "benchmark", ResolvedFingerprint = bundle.Fingerprint,
            Entries = new() { new RewardJournalEntry { EntryId = "coin", KindId = "currency", TargetId = "currency.coin", Amount = 1, Acknowledged = true } }
        }
    });
    model = Create(store, receipts + 1000);
    var inputs = Enumerable.Range(0, shots * perShot).Select(i => new MissionProgressEvent("shot", "event-" + i,
        "benchmark", model.WindowId, distinctValue: "value-" + i)).ToArray();
    int saves = store.Saves, notifications = 0;
    model.StateChanged += () => notifications++;
    var timer = new Stopwatch();
    long before = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
    if (batch) model.ReportProgressBatch(inputs);
    else foreach (var e in inputs) model.ReportProgress(e.EventKey, e.OriginalId, e.EventNamespace, e.WindowId, e.Amount, e.DistinctValue);
    timer.Stop(); long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    int commits = store.Saves - saves;
    if (commits != (batch ? 1 : inputs.Length) || notifications != commits ||
        model.GetState().Any(m => m.Progress != inputs.Length) || store.Data.PendingClaims.Count != 8)
        throw new InvalidOperationException("Benchmark changed transaction behavior.");
    if (output) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        receipts, shots, eventsPerShot = perShot, batch, repeat, store = serialize ? "json-serialization" : "reference",
        allocatedBytes = bytes, milliseconds = timer.Elapsed.TotalMilliseconds, commits, notifications,
        persistedCharacters = serialize ? store.Json.Length : (int?)null
    }));
}

static MissionModel Create(Store store, int limit)
{
    const string json = """
        {"catalogId":"benchmark","missions":[
          {"id":"counter","titleKey":"counter","eventKey":"shot","target":1000000,"rewardResourceId":"currency.coin","rewardAmount":1},
          {"id":"distinct","titleKey":"distinct","eventKey":"shot","target":1000000,"objective":"DistinctValue","rewardResourceId":"currency.coin","rewardAmount":1}
        ]}
        """;
    return new MissionModel(JsonConvert.DeserializeObject<MissionCatalogConfig>(json), store, new Wallet(), historyLimit: limit);
}

sealed class Wallet : IResourceWallet
{
    public bool TryGrant(ResourceGrantRequest request) => true;
    public bool TryExchange(ResourceExchangeRequest request) => false;
}

sealed class Store(bool serialize) : IDatasaveService
{
    public MissionSaveData Data;
    public string Json;
    public int Saves;
    public T Load<T>(string key = null) where T : SaveData, new() => Data == null ? new T() : (T)(SaveData)Data;
    public void Save<T>(T data, string key = null) where T : SaveData
    {
        Saves++; Data = (MissionSaveData)(SaveData)data;
        if (serialize) Json = JsonConvert.SerializeObject(data);
    }
    public void SaveAll() { }
    public bool Exists(string key) => Data != null;
    public void Delete(string key) => Data = null;
    public void DeleteAll() => Data = null;
}
