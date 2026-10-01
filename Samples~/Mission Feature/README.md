# Mission Feature

Install Dreamy UI and import this sample. Register the mission catalog before DataConfig initialization; install Missions after the persistent wallet and Datasave are ready. Instantiate Prefabs/MissionPanel.prefab under the game's Canvas with an EventSystem and PanelManager available. MissionController initializes and shows the panel using the registered IMissionService.

MissionPanel.prefab is a variant of Dreamy Feature's BaseFeaturePanel, inheriting SafeArea and the backdrop. MissionItem.prefab is a variant of BaseFeatureItem, with title, progress, reward and claim button, all wired. Host gameplay reports progress via IMissionService. Replace raw titleKey display with the game's localization adapter. Copy the Resources/DataConfig catalog to the game's DataConfig location if needed; avoid duplicate Resources paths.
