﻿using ProgressAdventure.Entity;
using ProgressAdventure.Enums;
using ProgressAdventure.SettingsManagement;
using ProgressAdventure.WorldManagement;
using SaveFileManager;
using System.Collections;
using System.Text;

namespace ProgressAdventure
{
    public static class SaveManager
    {
        #region Public functions
        /// <summary>
        /// Creates a save file from the save data.<br/>
        /// Makes a temporary backup.
        /// </summary>
        /// <param name="clearChunks">Whether to clear all chunks from the world object, after saving.</param>
        /// <param name="showProgressText">If not null, it writes out a progress percentage with this string while saving.</param>
        public static void MakeSave(bool clearChunks = true, string? showProgressText = null)
        {
            // make backup
            var backupStatus = Tools.CreateBackup(SaveData.saveName, true);
            // DATA FILE
            SaveDataFile();
            // CHUNKS/WORLD
            Tools.RecreateChunksFolder();
            Logger.Log("Saving chunks");
            World.SaveAllChunksToFiles(Tools.GetSaveFolderPath(), clearChunks, showProgressText);
            // remove backup
            if (backupStatus is not null)
            {
                File.Delete(backupStatus.Value.backupPath);
                Logger.Log("Removed temporary backup", backupStatus.Value.relativeBackupPath, LogSeverity.DEBUG);
            }
        }

        /// <summary>
        /// Creates the data for a new save file.
        /// </summary>
        public static void CreateSaveData()
        {
            Logger.Log("Preparing game data");
            // make save name
            var displaySaveName = Utils.Input("Name your save: ");
            var saveName = Tools.CorrectSaveName(displaySaveName);
            // make player
            var playerName = Utils.Input("What is your name?: ");
            var player = new Player(playerName);
            // load to class
            SaveData.Initialise(saveName, displaySaveName, player: player);
            World.Initialise();
            World.GenerateTile(SaveData.player.position.x, SaveData.player.position.y);
        }

        /// <summary>
        /// Loads a save file into the <c>SaveData</c> object.
        /// </summary>
        /// <param name="saveName">The name of the save folder.</param>
        /// <param name="backupChoice">If the user can choose, whether to backup the save, or not.</param>
        /// <param name="automaticBackup">If the save folder should be backed up or not. (only applies if <c>backupChoice</c> is false)</param>
        public static void LoadSave(string saveName, bool backupChoice = true, bool automaticBackup = true)
        {
            var saveFolderPath = Tools.GetSaveFolderPath(saveName);
            // get if save is a file
            Dictionary<string, object?>? data;
            if (Directory.Exists(saveFolderPath))
            {
                data = Tools.DecodeSaveShort(Path.Join(saveFolderPath, Constants.SAVE_FILE_NAME_DATA), 1);
            }
            else
            {
                Logger.Log("Not a valid save folder", $"folder name: {saveName}", LogSeverity.ERROR);
                throw new FileNotFoundException("Not a valid save folder", saveName);
            }
            // read data
    
            // auto backup
            if (!backupChoice && automaticBackup)
            {
                Tools.CreateBackup(saveName);
            }

            if (data is null)
            {
                Logger.Log("Unknown save version", $"save name: {saveName}", LogSeverity.ERROR);
                throw new FileLoadException("Unknown save version", saveName);
            }

            // save version
            string saveVersion;
            if (data.TryGetValue("saveVersion", out object? versionValue) && versionValue is not null)
            {
                saveVersion = (string)versionValue;
            }
            else
            {
                Logger.Log("Unknown save version", $"save name: {saveName}", LogSeverity.ERROR);
                throw new FileLoadException("Unknown save version", saveName);
            }

            if (saveVersion != Constants.SAVE_VERSION)
            {
                // backup
                if (backupChoice)
                {
                    var isOlder = Tools.IsUpToDate(saveVersion, Constants.SAVE_VERSION);
                    Logger.Log("Trying to load save with an incorrect version", $"{saveVersion} -> {Constants.SAVE_VERSION}", LogSeverity.WARN);
                    var ans = (int)new UIList(new string[] { "Yes", "No" }, $"\"{saveName}\" is {(isOlder ? "an older version" : "a newer version")} than what it should be! Do you want to backup the save before loading it?").Display(Settings.Keybinds.KeybindList);
                    if (ans == 0)
                    {
                        Tools.CreateBackup(saveName);
                    }
                }
                // correct
                CorrectSaveData(data, saveVersion);
            }
            // display_name
            var displayName = (string)data["displayName"];
            // last access
            var lastAccess = (DateTime)data["lastAccess"];
            // player
            var playerData = (IDictionary<string, object>)data["player"];
            var player = Player.FromJson(playerData);
            Logger.Log("Loaded save", $"save name: {saveName}, player name: \"{player.fullName}\", last saved: {Utils.MakeDate(lastAccess)} {Utils.MakeTime(lastAccess)}");

            // PREPARING
            Logger.Log("Preparing game data");
            // load seeds
            var seeds = (IDictionary<string, object?>)data["seeds"];
            var mainSeed = Tools.DeserializeRandom((string)seeds["mainRandom"]);
            var worldSeed = Tools.DeserializeRandom((string)seeds["worldRandom"]);
            var tileTypeNoiseSeeds = seeds["tileTypeNoiseSeeds"];
            var deserialisedNoiseSeeds = DeserialiseTileNoiseSeeds((IDictionary<string, object?>)tileTypeNoiseSeeds);
            // load to class
            SaveData.Initialise(saveName, displayName, lastAccess, player, mainSeed, worldSeed, deserialisedNoiseSeeds);
            World.Initialise();
        }

        /// <summary>
        /// Gets all save files from the save folder, and proceses them for display.
        /// </summary>
        public static List<(string saveName, string displayText)> GetSavesData()
        {
            Tools.RecreateSavesFolder();
            // read saves
            var folders = GetSaveFolders();
            var datas = GetFoldersDisplayData(folders);
            // process file data
            var datasProcessed = new List<(string saveName, string displayText)>();
            foreach (var data in datas)
            {
                if (data.data is null)
                {
                    Logger.Log("Decode error", $"save name: {data.folderName}", LogSeverity.ERROR);
                    Utils.PressKey($"\"{data.folderName}\" is corrupted!");
                }
                else
                {
                    var processedData = ProcessSaveDisplayData(data);
                    if (processedData is not null)
                    {
                        datasProcessed.Add(((string saveName, string displayText))processedData);
                    }
                }
            }
            return datasProcessed;
        }
        #endregion

        #region Private functions
        /// <summary>
        /// Creates the data file part of a save file from the save data.
        /// </summary>
        private static void SaveDataFile()
        {
            // FOLDER
            Tools.RecreateSaveFileFolder();
            var saveFolderPath = Tools.GetSaveFolderPath();
            // DATA FILE
            var displayDataJson = SaveData.DisplayDataToJson();
            var saveDataJson = SaveData.MainDataToJson();
            // create new save
            Tools.EncodeSaveShort(new List<IDictionary> { displayDataJson, saveDataJson }, Path.Join(saveFolderPath, Constants.SAVE_FILE_NAME_DATA));
        }

        /// <summary>
        /// Modifies the save data, to make it up to date, with the newest save file data structure.
        /// </summary>
        /// <param name="jsonData">The json representation of the jave data.</param>
        /// <param name="saveVersion">The original version of the save file.</param>
        private static void CorrectSaveData(Dictionary<string, object?> jsonData, string saveVersion)
        {
            Logger.Log("Correcting save data");
            // 1.5.3 -> 2.0
            if (saveVersion == "1.5.3")
            {
                // exported from python
                Logger.Log("Correcting save data from python export", "1.5.3 -> 2.0", LogSeverity.INFO);
                throw new NotImplementedException("Converting save from python export, is not done yet!");
                saveVersion = "2.0";
                Logger.Log("Corrected save data", "1.5.3 -> 2.0", LogSeverity.DEBUG);
            }
        }

        /// <summary>
        /// Deserialises the json representation of the tile type noise seeds, into a potentialy partial dictionary.
        /// </summary>
        /// <param name="tileTypeNoiseSeeds">The json representation of the tile type noise seeds.</param>
        private static Dictionary<TileNoiseType, ulong> DeserialiseTileNoiseSeeds(IDictionary<string, object?> tileTypeNoiseSeeds)
        {
            var noiseSeedDict = new Dictionary<TileNoiseType, ulong>();
            foreach (var tileTypeNoiseSeed in tileTypeNoiseSeeds)
            {
                if (
                    tileTypeNoiseSeed.Value is not null &&
                    Enum.TryParse(typeof(TileNoiseType), tileTypeNoiseSeed.Key.ToString(), out object? noiseTypeValue) &&
                    noiseTypeValue is not null &&
                    Enum.IsDefined(typeof(TileNoiseType), noiseTypeValue) &&
                    uint.TryParse(tileTypeNoiseSeed.Value.ToString(), out uint noiseSeed)
                )
                {
                    noiseSeedDict.Add((TileNoiseType)noiseTypeValue, noiseSeed);
                }
            }
            return noiseSeedDict;
        }

        /// <summary>
        /// Turns the json display data from a json into more uniform data.
        /// </summary>
        /// <param name="data"></param>
        private static (string saveName, string displayText)? ProcessSaveDisplayData((string folderName, Dictionary<string, object?>? data) data)
        {
            try
            {
                if (data.data is not null)
                {
                    var displayText = new StringBuilder();
                    var displayName = data.data["displayName"] ?? data.folderName;
                    displayText.Append($"{displayName}: {data.data["playerName"]}\n");
                    var lastAccess = (DateTime)(data.data["lastAccess"] ?? DateTime.Now);
                    displayText.Append($"Last opened: {Utils.MakeDate(lastAccess, ".")} {Utils.MakeTime(lastAccess)}");
                    // check version
                    var saveVersion = (string)(data.data["saveVersion"] ?? "[UNKNOWN VERSION]");
                    displayText.Append(Utils.StylizedText($" v.{saveVersion}", saveVersion == Constants.SAVE_VERSION ? Constants.Colors.GREEN : Constants.Colors.RED));
                    return (data.folderName, displayText.ToString());
                }
                else
                {
                    throw new ArgumentException("No data in save file.");
                }
            }
            catch (Exception ex)
            {
                if (ex is InvalidCastException || ex is ArgumentException || ex is KeyNotFoundException)
                {
                    Logger.Log("Parse error", $"Save name: {data.folderName}", LogSeverity.ERROR);
                    Utils.PressKey($"\"{data.folderName}\" could not be parsed!");
                    return null;
                }
                throw;
            }
        }

        /// <summary>
        /// Gets all folders from the saves folder.
        /// </summary>
        private static List<string> GetSaveFolders()
        {
            var folders = new List<string>();
            var folderPaths = Directory.GetDirectories(Constants.SAVES_FOLDER_PATH);
            var dataFileName = $"{Constants.SAVE_FILE_NAME_DATA}.{Constants.SAVE_EXT}";
            foreach (var folderPath in folderPaths)
            {
                if (File.Exists(Path.Join(folderPath, dataFileName)))
                {
                    folders.Add(Path.GetFileName(folderPath));
                }
            }
            folders.Sort();
            return folders;
        }

        /// <summary>
        /// Gets the display data from all save files in the saves folder.
        /// </summary>
        /// <param name="folders">A list of valid save folders.</param>
        /// <returns>A list of tuples, containing the folder name, and the data in it. The data will be null, if the folder wasn't readable.</returns>
        private static List<(string folderName, Dictionary<string, object?>? data)> GetFoldersDisplayData(IEnumerable<string> folders)
        {
            var datas = new List<(string folderName, Dictionary<string, object?>? data)>();
            foreach (var folder in folders)
            {
                Dictionary<string, object?>? data = null;
                try
                {
                    data = Tools.DecodeSaveShort(Path.Join(Tools.GetSaveFolderPath(folder), Constants.SAVE_FILE_NAME_DATA), 0);
                }
                catch (FormatException) { }
                datas.Add((folder, data));
            }
            return datas;
        }
        #endregion
    }
}