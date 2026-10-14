using System;
using System.IO;
using System.Linq;
using Dreamy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.PeriodicSample.Editor
{
    public static class PeriodicMissionSampleBuilder
    {
        [MenuItem("Tools/Dreamy/Missions/Build Periodic Sample Prefabs")]
        public static void BuildImportedSample()
        {
            string script = AssetDatabase.FindAssets("PeriodicMissionSampleBuilder t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Single(path => path.EndsWith("/Editor/PeriodicMissionSampleBuilder.cs", StringComparison.Ordinal));
            Build(Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/'),
                TMP_Settings.defaultFontAsset);
        }

        public static void Build(string folder, TMP_FontAsset font)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit PlayMode and Prefab Stage before building sample assets.");
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("Choose the imported Periodic Missions folder.", nameof(folder));
            if (font == null) throw new ArgumentNullException(nameof(font));
            var daily = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/periodicDailyMissionCatalog.json");
            var weekly = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/periodicWeeklyMissionCatalog.json");
            if (daily == null || weekly == null) throw new InvalidOperationException("Import both periodic catalogs first.");
            if (!AssetDatabase.IsValidFolder(folder + "/Prefabs")) AssetDatabase.CreateFolder(folder, "Prefabs");

            var row = Rect("PeriodicMissionItem", null);
            row.gameObject.SetActive(false);
            PeriodicMissionItem itemAsset;
            try
            {
                row.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.17f, 0.25f, 1);
                var layout = Vertical(row, 12);
                layout.spacing = 4;
                Size(row, 244);
                var item = row.gameObject.AddComponent<PeriodicMissionItem>();
                Set(item, "titleText", Label(row, "Title", "Mission", 44, font));
                Set(item, "progressText", Label(row, "Progress", "0 / 1", 32, font));
                Set(item, "rewardText", Label(row, "Reward", "Reward bundle", 64, font));
                var actions = Horizontal(row, "Actions", 60);
                Set(item, "claimButton", Button(actions, "Claim", "Claim / Retry", font));
                Set(item, "rerollButton", Button(actions, "Reroll", "Free Reroll", font));
                Set(item, "claimLabel", actions.Find("Claim/Label").GetComponent<TMP_Text>());
                var saved = PrefabUtility.SaveAsPrefabAsset(row.gameObject, folder + "/Prefabs/PeriodicMissionItem.prefab");
                itemAsset = saved.GetComponent<PeriodicMissionItem>();
            }
            finally { UnityEngine.Object.DestroyImmediate(row.gameObject); }

            var root = Rect("PeriodicMissionPanel", null);
            root.gameObject.SetActive(false);
            try
            {
                Stretch(root);
                root.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.075f, 1);
                var group = root.gameObject.AddComponent<CanvasGroup>();
                var panel = root.gameObject.AddComponent<PeriodicMissionPanel>();
                var controller = root.gameObject.AddComponent<PeriodicMissionDemoController>();
                var items = root.gameObject.AddComponent<DemoPeriodicItemDestination>();
                Set(panel, "canvasGroup", group);
                var safe = Rect("SafeArea", root); Stretch(safe);
                safe.gameObject.AddComponent<SafeArea>();
                var content = Rect("Content", safe); Stretch(content);
                content.offsetMin = new Vector2(24, 24); content.offsetMax = new Vector2(-24, -24);
                Vertical(content, 12);
                Set(panel, "headingText", Label(content, "Heading", "Daily Missions", 56, font));
                Set(panel, "countdownText", Label(content, "Countdown", "Waiting for configured clock", 36, font));
                Set(panel, "rerollText", Label(content, "RerollState", "1 free reroll remaining", 32, font));
                var tabs = Horizontal(content, "Tabs", 64);
                Set(panel, "dailyTabButton", Button(tabs, "Daily", "Daily", font));
                Set(panel, "weeklyTabButton", Button(tabs, "Weekly", "Weekly", font));
                Set(panel, "closeButton", Button(tabs, "Back", "Back", font));
                var simulate = Horizontal(content, "Simulate", 64);
                Set(panel, "winButton", Button(simulate, "Win", "Demo Stage Win", font));
                Set(panel, "collectButton", Button(simulate, "Collect", "Demo Collect 100", font));
                var recovery = Horizontal(content, "Recovery", 64);
                Set(panel, "retryButton", Button(recovery, "Retry", "Retry Pending", font));
                Set(panel, "refreshButton", Button(recovery, "Refresh", "Refresh Window", font));
                var failures = Horizontal(content, "DemoFailures", 48);
                Set(items, "pauseDeliveriesToggle", Toggle(failures, "Pause", "Pause demo item", font));
                Set(items, "rejectDeliveriesToggle", Toggle(failures, "Reject", "Reject demo item", font));
                Set(items, "balanceText", Label(content, "DemoBalance", "DEMO tokens owned: 0", 32, font));
                Set(panel, "statusText", Label(content, "Status", "Configure this sample before activation.", 64, font));

                var scroll = Rect("MissionScroll", content);
                Size(scroll, 180).flexibleHeight = 1;
                var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
                scrollRect.horizontal = false; scrollRect.scrollSensitivity = 50;
                var viewport = Rect("Viewport", scroll); Stretch(viewport);
                viewport.gameObject.AddComponent<RectMask2D>();
                var rows = Rect("Rows", viewport);
                rows.anchorMin = new Vector2(0, 1); rows.anchorMax = Vector2.one;
                rows.pivot = new Vector2(0.5f, 1); rows.sizeDelta = Vector2.zero;
                Vertical(rows, 0);
                rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scrollRect.viewport = viewport; scrollRect.content = rows;
                Set(panel, "itemContainer", rows); Set(panel, "itemPrefab", itemAsset);
                Set(controller, "panel", panel);
                Set(controller, "dailyCatalog", daily); Set(controller, "weeklyCatalog", weekly);
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, folder + "/Prefabs/PeriodicMissionPanel.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
            Debug.Log("[PeriodicMissions] Built only the two sample prefabs in " + folder);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            return rect;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }
        private static LayoutElement Size(RectTransform rect, float height)
        {
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = height; size.preferredHeight = height; size.flexibleHeight = 0;
            return size;
        }
        private static VerticalLayoutGroup Vertical(RectTransform rect, int padding)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = 8;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            return layout;
        }
        private static RectTransform Horizontal(Transform parent, string name, float height)
        {
            var row = Rect(name, parent); Size(row, height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            return row;
        }
        private static TMP_Text Label(Transform parent, string name, string text, float height, TMP_FontAsset font)
        {
            var rect = Rect(name, parent); Size(rect, height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = 32;
            label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 32;
            label.color = Color.white; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }
        private static Button Button(Transform parent, string name, string text, TMP_FontAsset font)
        {
            var rect = Rect(name, parent); Size(rect, 60);
            rect.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.35f, 0.55f, 1);
            var button = rect.gameObject.AddComponent<Button>();
            var label = Label(rect, "Label", text, 36, font);
            Stretch((RectTransform)label.transform);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        private static Toggle Toggle(Transform parent, string name, string text, TMP_FontAsset font)
        {
            var rect = Rect(name, parent); Size(rect, 44);
            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.17f, 0.25f, 1);
            var toggle = rect.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = background;
            var check = Rect("Check", rect);
            check.anchorMin = check.anchorMax = new Vector2(0, 0.5f);
            check.sizeDelta = new Vector2(22, 22); check.anchoredPosition = new Vector2(22, 0);
            var mark = check.gameObject.AddComponent<Image>(); mark.color = Color.cyan; mark.raycastTarget = false;
            toggle.graphic = mark; toggle.isOn = false;
            var label = Label(rect, "Label", text, 36, font);
            Stretch((RectTransform)label.transform);
            ((RectTransform)label.transform).offsetMin = new Vector2(44, 0);
            return toggle;
        }
        private static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var field = serialized.FindProperty(name);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name);
            field.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
