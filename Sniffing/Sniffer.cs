using RockSnifferLib.Cache;
using RockSnifferLib.Configuration;
using RockSnifferLib.Events;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.NoteData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace RockSnifferLib.Sniffing
{
    public class Sniffer
    {
        /// <summary>
        /// Fired when the Sniffer state has changed
        /// </summary>
        public event EventHandler<OnStateChangedArgs> OnStateChanged;

        /// <summary>
        /// Fired when the current song details have changed
        /// </summary>
        public event EventHandler<OnSongChangedArgs> OnSongChanged;

        /// <summary>
        /// Fired after each successful memory readout
        /// </summary>
        public event EventHandler<OnMemoryReadoutArgs> OnMemoryReadout;

        /// <summary>
        /// Fired when a selected song starts loading, before gameplay begins
        /// </summary>
        public event EventHandler<OnSongSelectedArgs> OnSongSelected;

        /// <summary>
        /// Fired once per run when gameplay starts
        /// </summary>
        public event EventHandler<OnSongStartedArgs> OnSongStarted;

        /// <summary>
        /// Fired once per run when gameplay ends: completed, quit, restarted, or
        /// replaced by the next song in Nonstop Play
        /// </summary>
        public event EventHandler<OnSongEndedArgs> OnSongEnded;

        /// <summary>
        /// Fired when a new psarc file is added to the dlc folder
        /// </summary>
        public event EventHandler<OnPsarcInstalledArgs> OnPsarcInstalled;

        /// <summary>
        /// The current state of rocksmith, initial state is IN_MENUS
        /// </summary>
        public SnifferState currentState = SnifferState.NONE;
        private SnifferState previousState = SnifferState.NONE;

        /// <summary>
        /// Currently active cdlc details
        /// </summary>
        private SongDetails currentCDLCDetails = new SongDetails();

        /// <summary>
        /// Currently active memory readout
        /// </summary>
        private RSMemoryReadout currentMemoryReadout = new RSMemoryReadout();

        // Timer tracking for the current run. initTime is just past the lowest timer
        // value seen, so a restart (timer back near the start) can be detected.
        private float lowTime = float.MaxValue;
        private float initTime = float.MaxValue;
        private float maxTime = float.MinValue;
        private bool paused = false;

        // Timer when the pause started. On Exit from the pause menu the overlay closes
        // while the timer stays put, which must not count as a resume.
        private float pauseTimerSnapshot = float.MinValue;

        // A pause starts when the overlay opens, not on the raw value: after Restart
        // from the pause menu the byte stays TopOverlay for a moment while the new
        // attempt is already playing.
        private PauseMenuMode previousPauseMenuMode = PauseMenuMode.None;

        // Arrangement and mode of the running song, taken at its start. In Nonstop Play
        // the readout can move on to the next song before this one ends.
        private string currentSongRunArrangementID = null;
        private string currentSongRunPath = null;
        private string currentSongRunTuning = null;
        private bool currentSongRunWasNonstopMode = false;
        private bool currentSongRunWasMultiplayerMode = false;

        // Last readout taken during play, used for the end of the run. On Restart,
        // Rocksmith resets the note data for the new attempt before the old one ends.
        private RSMemoryReadout lastInRunReadout = null;

        // songID of the run that was started / ended. Several paths can try to start or
        // end the same run; each run starts and ends once.
        private string songRunStartedForID = null;
        private string songRunEndedForID = null;

        // A run whose timer never got past this was aborted before playback (e.g. no
        // input detected). Also above the near-zero timer values seen while loading.
        private const float MIN_PROGRESS_SECONDS = 0.1f;

        private string lastGameStage = null;

        /// <summary>
        /// Reference to the rocksmith process
        /// </summary>
        private readonly Process _rsProcess;
        
        /// <summary>
        /// Which _edition of Rocksmith we are attached to
        /// </summary>
        private readonly RSEdition _edition;

        /// <summary>
        /// Cache to use
        /// </summary>
        private readonly ICache _cache;

        /// <summary>
        /// The memory reader
        /// </summary>
        private readonly RSMemoryReader memReader;

        /// <summary>
        /// Settings this sniffer was instantiated with
        /// </summary>
        private readonly SnifferSettings _settings;

        /// <summary>
        /// Boolean to let async tasks finish
        /// </summary>
        private bool running = true;

        /// <summary>
        /// FileSystemWatchers to watch the dlc folder (and any symlinks)
        /// </summary>
        private List<FileSystemWatcher> fileSystemWatchers = new List<FileSystemWatcher>();

        /// <summary>
        /// An ActionBlock for processing psarc files
        /// </summary>
        private ActionBlock<string> psarcFileBlock;

        /// <summary>
        /// Instantiate a new Sniffer on process, using cache
        /// </summary>
        /// <param name="rsProcess"></param>
        /// <param name="cache"></param>
        /// <param name="edition"></param>
        /// <param name="settings"></param>
        public Sniffer(Process rsProcess, ICache cache, RSEdition edition, SnifferSettings? settings = null)
        {
            //Use default settings if no settings were given
            settings ??= new SnifferSettings();

            _rsProcess = rsProcess;
            _cache = cache;
            _edition = edition;
            _settings = settings;

            //Initialize memory reader
            memReader = new RSMemoryReader(_rsProcess, _edition);

            OnStateChanged += Sniffer_OnStateChanged;

            //Listen to PsarcInstalled event for auto enumeration
            if (settings.enableAutoEnumeration)
            {
                OnPsarcInstalled += Sniffer_OnPsarcInstalled;
            }

            DoMemoryReadout();
            DoStateMachine();
            DoSniffing();
        }

        /// <summary>
        /// Trigger enumeration when a new psarc file is installed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sniffer_OnPsarcInstalled(object sender, OnPsarcInstalledArgs e)
        {
            Logger.Log("New PSARC file installed: {0}", e.FilePath);
            TriggerEnumeration();
        }

        /// <summary>
        /// Trigger the enumerate flag, causing rocksmith to start enumerating
        /// </summary>
        public void TriggerEnumeration()
        {
            memReader.TriggerEnumeration();
        }

        /// <summary>
        /// Handle specific events based on state changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sniffer_OnStateChanged(object sender, OnStateChangedArgs e)
        {
            var newState = e.newState;
            var oldState = e.oldState;

            if (oldState is SnifferState.IN_MENUS or SnifferState.SONG_SELECTED &&
                newState is SnifferState.SONG_STARTING or SnifferState.SONG_PLAYING)
            {
                OnSongSelected?.Invoke(this, new OnSongSelectedArgs { song = currentCDLCDetails });
            }
        }

        private async void DoMemoryReadout()
        {
            while (running)
            {
                await Task.Delay(100);

                RSMemoryReadout newReadout = null;

                try
                {
                    //Read data from memory
                    newReadout = memReader.DoReadout();
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while reading memory: {0} {1}\r\n{2}", e.GetType(), e.Message, e.StackTrace);
                    }
                }

                if (newReadout == null)
                {
                    continue;
                }

                if (newReadout.songID != currentMemoryReadout.songID || (currentCDLCDetails == null || !currentCDLCDetails.IsValid()))
                {
                    // End the previous song's run before switching songs. In Nonstop Play
                    // the state machine can stay in SONG_ENDING and never end it itself.
                    if (CanEndSongRun())
                    {
                        EndSongRun(DetermineCompletedForForceEnd());
                        currentState = SnifferState.IN_MENUS;
                        ResetRunTiming();
                    }

                    var newDetails = _cache.Get(newReadout.songID);

                    if (newDetails != null && newDetails.IsValid())
                    {
                        currentCDLCDetails = _cache.Get(newReadout.songID);
                        OnSongChanged?.Invoke(this, new OnSongChangedArgs { songDetails = currentCDLCDetails });
                        currentCDLCDetails.Print();

                        ResetRunTiming();
                        ResetSongRun();
                        songRunEndedForID = null;
                    }

                }

                // After a song change the memory can still hold the previous song's
                // arrangement ID, so drop IDs that don't belong to the current song
                if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                    !string.IsNullOrEmpty(newReadout.arrangementID) &&
                    !currentCDLCDetails.arrangements.Any(a => a.arrangementID == newReadout.arrangementID))
                {
                    newReadout.arrangementID = null;
                }

                previousPauseMenuMode = currentMemoryReadout?.pauseMenuMode ?? PauseMenuMode.None;

                newReadout.CopyTo(ref currentMemoryReadout);

                // Only take snapshots while the timer moves forward. After a Restart the
                // state can still read SONG_PAUSED while the new attempt's timer climbs
                // from the start, and its reset note data must not replace the old
                // attempt's. A resume also rewinds the timer a little.
                if ((currentState == SnifferState.SONG_STARTING ||
                     currentState == SnifferState.SONG_PLAYING ||
                     currentState == SnifferState.SONG_PAUSED ||
                     currentState == SnifferState.SONG_ENDING) &&
                    currentMemoryReadout.songTimer > 0 &&
                    (lastInRunReadout == null ||
                     currentMemoryReadout.songTimer >= lastInRunReadout.songTimer))
                {
                    lastInRunReadout = currentMemoryReadout.Clone();
                }

                if (currentMemoryReadout.songTimer >= 0.001f)
                {
                    // One poll interval past the lowest timer seen
                    if (lowTime == float.MaxValue || currentMemoryReadout.songTimer < lowTime)
                    {
                        lowTime = currentMemoryReadout.songTimer;
                        initTime = currentMemoryReadout.songTimer + 0.101f;
                    }

                    maxTime = Math.Max(maxTime, currentMemoryReadout.songTimer);
                }

                // The timer-based state machine can miss song changes in Nonstop Play,
                // so also start and end runs on its gameStage changes
                string currentGameStage = currentMemoryReadout.gameStage;
                if (currentGameStage != lastGameStage)
                {
                    string prevStage = lastGameStage;
                    string newStage = currentGameStage;

                    // Usually the songID change above ends the run first
                    if (prevStage == "nonstopplaygame" && newStage == "nonstopplayhub")
                    {
                        if (CanEndSongRun())
                        {
                            EndSongRun(DetermineCompletedForForceEnd());
                            currentState = SnifferState.IN_MENUS;
                            ResetRunTiming();
                        }
                    }
                    // The timer can flash non-zero while loading, so wait for it to pass initTime
                    else if (prevStage == "nonstopplayhub" && newStage == "nonstopplaygame")
                    {
                        if (CanBeginSongRun() &&
                            initTime != float.MaxValue &&
                            currentMemoryReadout.songTimer > initTime)
                        {
                            BeginSongRun();
                            currentState = SnifferState.SONG_PLAYING;
                        }
                    }

                    lastGameStage = newStage;
                }

                OnMemoryReadout?.Invoke(this, new OnMemoryReadoutArgs() { memoryReadout = currentMemoryReadout });

                //Print memreadout if debug is enabled
                currentMemoryReadout.Print();
            }
        }

        private async void DoStateMachine()
        {
            while (running)
            {
                try
                {
                    //Update the state
                    UpdateState();
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while processing state machine: {0} {1}", e.GetType(), e.Message);
                    }
                }

                //Delay for 100 milliseconds
                await Task.Delay(100);
            }
        }

        private void CreateFileSystemWatcher(string path, string filter)
        {
            var watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = true,

                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,

                //Increase buffer size to 64k to avoid losing files
                InternalBufferSize = 1024 * 64
            };

            watcher.Created += PsarcFileChanged;
            watcher.Changed += PsarcFileChanged;
            watcher.Renamed += PsarcFileChanged;
            watcher.Error += Watcher_Error;

            watcher.EnableRaisingEvents = true;

            fileSystemWatchers.Add(watcher);

            Logger.Log("Created FileSystemWatcher for {0}", path);
        }

        private void FindSymLinks(string path, List<string> symlinks)
        {
            // Get all directories
            var dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);

            // Go through all found directories
            foreach(var dir in dirs)
            {
                // Check if path has the reparsepoint attribute (it is most likely a symlink)
                if(new FileInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Logger.Log($"Found symlink at {dir}");
                    symlinks.Add(dir);
                }
            }
        }

        private async void DoSniffing()
        {
            // Get path to rs directory
            var path = Path.GetDirectoryName(_rsProcess.MainModule.FileName);

            // Create main watcher for the dlc folder
            CreateFileSystemWatcher(path + Path.DirectorySeparatorChar + "dlc", "*.psarc");

            // Find all symbolic links and create a watcher for each
            var symlinks = new List<string>();
            FindSymLinks(path + Path.DirectorySeparatorChar + "dlc", symlinks);

            // Create a watcher for each symlink
            foreach (var symlink in symlinks) CreateFileSystemWatcher(symlink, "*.psarc");

            // Clamp to max 8 parallelism, because going higher is pretty ridiculous
            // Going higher is still possible manually through the config
            int parallelism = Math.Min(8, Math.Max(1, Environment.ProcessorCount));

            //Use parallelism value from settings
            if (_settings.parallelism > 0) parallelism = _settings.parallelism;

            Logger.Log("Using parallelism of {0}", parallelism);
            psarcFileBlock = new ActionBlock<string>(psarcFile => ProcessPsarcFile(psarcFile), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = parallelism });

            await Task.Run(() => ProcessAllPsarcs(path));
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            Logger.LogError("FileSystemWatcher Error: {0}", e.GetException().Message);
            Logger.LogException(e.GetException());
        }

        /// <summary>
        /// Queue to keep track of files that are due for parsing
        /// to avoid parsing the same file multiple times
        /// </summary>
        private static List<string> processingQueue = new List<string>();
        private void PsarcFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Logger.logProcessingQueue) Logger.Log("FileSystemWatcher: {0} \"{1}\"", e.ChangeType, e.Name);

            var psarcFile = e.FullPath;

            //Avoid duplicates in the block
            if (processingQueue.Contains(psarcFile)) return;

            processingQueue.Add(psarcFile);

            //Add to block to process the psarc file
            bool posted = psarcFileBlock.Post(psarcFile);

            //If post was not successful
            if (!posted) Logger.LogError("Unable to post {0} to psarcFileBlock", psarcFile);

            if (Logger.logProcessingQueue) Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);

        }

        private void PsarcFileProcessingDone(string psarcFile, bool success)
        {
            //If file was in the queue (triggered by filesystemwatcher)
            if (processingQueue.Contains(psarcFile))
            {
                //If processing was successful, invoke event
                OnPsarcInstalled?.Invoke(this, new OnPsarcInstalledArgs() { FilePath = psarcFile, ParseSuccess = success });

                //Remove from queue
                processingQueue.Remove(psarcFile);
            }

            if (Logger.logProcessingQueue)
            {
                Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);
            }
        }

        private void ProcessPsarcFile(string psarcFile)
        {
            var fileInfo = new FileInfo(psarcFile);

            // Try to hash the psarc file
            string hash;
            try
            {
                hash = PSARCUtil.GetFileHash(fileInfo);
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to calculate hash for {0}", psarcFile);
                Logger.LogException(e);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //Return if file is already cached
            if (_cache.Contains(psarcFile, hash))
            {
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //Read psarc data
            Dictionary<string, SongDetails> allSongDetails;
            try
            {
                allSongDetails = PSARCUtil.ReadPSARCHeaderData(fileInfo, hash);
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to read {0}", psarcFile);
                Logger.LogException(e);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //If loading was successful
            if (allSongDetails != null)
            {
                //In case file hash was different
                //or if this is a newer psarc with the same song ids
                //Remove all existing entries
                _cache.Remove(psarcFile, allSongDetails.Keys.ToList());

                //Add this CDLC file to the cache
                _cache.Add(psarcFile, allSongDetails);
            }

            PsarcFileProcessingDone(psarcFile, true);
        }

        private void ProcessAllPsarcs(string path)
        {
            //Build a list of all dlc psarc files, including songs.psarc
            List<string> psarcFiles = new List<string>
            {
                path + $"{Path.DirectorySeparatorChar}songs.psarc"
            };

            //Go into the dlc folder
            path += $"{Path.DirectorySeparatorChar}dlc";

            GetAllPsarcFiles(path, psarcFiles);

            foreach (string psarcFile in psarcFiles)
            {
                psarcFileBlock.Post(psarcFile);
            }

            Logger.Log("Found {0} psarc files", psarcFiles.Count);
        }

        private void GetAllPsarcFiles(string path, List<string> files)
        {
            //Add all files in the current path including all subdirectories
            files.AddRange(Directory.GetFiles(path, "*_p.psarc", SearchOption.AllDirectories));
        }

        /// <summary>
        /// Stops the sniffer, stopping all async tasks
        /// </summary>
        public void Stop()
        {
            running = false;

            foreach (var watcher in fileSystemWatchers)
            {
                watcher.Dispose();
            }

            fileSystemWatchers.Clear();
        }

        /// <summary>
        /// Starts a song run: resolves the arrangement, captures the run context and
        /// fires OnSongStarted. Ignored if this run has already started.
        /// </summary>
        private void BeginSongRun()
        {
            if (!CanBeginSongRun())
            {
                return;
            }

            ArrangementDetails? arrangement = ResolveRunArrangement(out string? fallbackReason);
            CaptureSongRunStart(arrangement);

            OnSongStarted?.Invoke(this, new OnSongStartedArgs
            {
                song = currentCDLCDetails,
                timestamp = DateTime.Now,
                arrangementID = currentSongRunArrangementID,
                path = currentSongRunPath,
                tuning = currentSongRunTuning,
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode
            });

            LogArrangementFallback(arrangement, fallbackReason);
        }

        private bool CanBeginSongRun()
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return false;
            }

            if (songRunStartedForID != null &&
                songRunStartedForID == currentCDLCDetails.songID)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Picks the arrangement being played. fallbackReason is null for a direct
        /// arrangementID match and names the fallback otherwise. Returns null when
        /// nothing can be picked unambiguously.
        /// </summary>
        private ArrangementDetails? ResolveRunArrangement(out string? fallbackReason)
        {
            fallbackReason = null;

            var arrangements = currentCDLCDetails.arrangements;
            if (arrangements == null || arrangements.Count == 0)
            {
                return null;
            }

            var arrangement = arrangements.FirstOrDefault(a => a.arrangementID == currentMemoryReadout.arrangementID);
            if (arrangement != null)
            {
                return arrangement;
            }

            // The selected Path only gives the type, so prefer the main arrangement
            // over bonus and alternate ones
            string currentPath = null;
            if (currentMemoryReadout.currentPath != RSPath.Unknown)
            {
                currentPath = currentMemoryReadout.currentPath.ToString();
            }

            if (currentPath != null)
            {
                var pathMatches = arrangements.Where(a => a.type == currentPath || a.name == currentPath).ToList();
                arrangement = pathMatches.FirstOrDefault(a => !a.isBonusArrangement && !a.isAlternateArrangement)
                    ?? pathMatches.FirstOrDefault();

                if (arrangement != null)
                {
                    fallbackReason = $"current Path {currentPath}";
                    return arrangement;
                }
            }

            var playable = arrangements.Where(a => !a.isBonusArrangement && !a.isAlternateArrangement).ToList();
            if (playable.Count == 1)
            {
                fallbackReason = "only playable arrangement";
                return playable[0];
            }

            if (arrangements.Count == 1)
            {
                fallbackReason = "only arrangement";
                return arrangements[0];
            }

            return null;
        }

        private void CaptureSongRunStart(ArrangementDetails? arrangement)
        {
            string startGameStage = currentMemoryReadout?.gameStage;
            currentSongRunWasNonstopMode =
                startGameStage == "nsp_main" ||
                startGameStage == "nonstopplayhub" ||
                startGameStage == "nonstopplaygame";
            currentSongRunWasMultiplayerMode = currentMemoryReadout?.mode == RSMode.MULTIPLAYER;

            if (arrangement != null)
            {
                currentSongRunArrangementID = arrangement.arrangementID;
                currentSongRunPath = arrangement.type;
                currentSongRunTuning = arrangement.tuning.TuningName;
            }
            else
            {
                currentSongRunArrangementID = null;
                currentSongRunPath = "unknown";
                currentSongRunTuning = "unknown";
            }

            songRunStartedForID = currentCDLCDetails.songID;

            // A replay of the same song has the same songID
            songRunEndedForID = null;

            // So a run that ends right away doesn't report the previous run's stats
            lastInRunReadout = currentMemoryReadout?.Clone();
        }

        private void LogArrangementFallback(ArrangementDetails? arrangement, string? fallbackReason)
        {
            if (arrangement == null)
            {
                int arrCount = currentCDLCDetails.arrangements?.Count ?? 0;
                Logger.LogError(
                    "Could not resolve the arrangement for {0} (arrangementID '{1}', {2} arrangements), path and tuning will be 'unknown'",
                    currentCDLCDetails.songID ?? "<unknown>",
                    currentMemoryReadout.arrangementID ?? "<null>",
                    arrCount);
            }
            // In Nonstop Play the ID match routinely fails at song changes
            else if (fallbackReason != null && !currentSongRunWasNonstopMode)
            {
                Logger.LogError(
                    "Arrangement ID '{0}' not found, picked by {1}: path {2}, tuning {3}",
                    currentMemoryReadout.arrangementID ?? "<null>",
                    fallbackReason,
                    currentSongRunPath,
                    currentSongRunTuning);
            }
        }

        /// <summary>
        /// Completed flag for runs that end without passing through SONG_ENDING.
        /// Counts as completed unless the song was failed, aborted before playback,
        /// or quit from the pause menu.
        /// </summary>
        private bool DetermineCompletedForForceEnd()
        {
            // Score Attack fail, also on the last phrase
            if (currentMemoryReadout?.mode == RSMode.SCOREATTACK &&
                currentMemoryReadout.noteData is ScoreAttackNoteData saData &&
                saData.FailedPhrases >= 3)
            {
                return false;
            }

            if (maxTime < MIN_PROGRESS_SECONDS)
            {
                return false;
            }

            // Quit, restart or skip from the pause menu
            if (currentState == SnifferState.SONG_PAUSED)
            {
                return false;
            }

            // E.g. the poll missed SONG_ENDING, or Nonstop Play moved to the next song
            return true;
        }

        /// <summary>
        /// Ends the running song run and fires OnSongEnded. Ignored if no run started
        /// for the current song or the run has already ended.
        /// </summary>
        private void EndSongRun(bool completed)
        {
            if (!CanEndSongRun())
            {
                return;
            }

            OnSongEndedArgs args = CaptureSongRunEnd(completed);

            // Set before firing, so handlers can't end the run again
            songRunEndedForID = args.song.songID;
            OnSongEnded?.Invoke(this, args);

            ResetSongRun();
        }

        private bool CanEndSongRun()
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return false;
            }

            // No end without a start, e.g. quit before the timer moved
            if (songRunStartedForID != currentCDLCDetails.songID)
            {
                return false;
            }

            if (songRunEndedForID != null &&
                songRunEndedForID == currentCDLCDetails.songID)
            {
                return false;
            }

            return true;
        }

        private OnSongEndedArgs CaptureSongRunEnd(bool completed)
        {
            RSMemoryReadout readout = (lastInRunReadout ?? currentMemoryReadout)?.Clone();

            return new OnSongEndedArgs
            {
                song = currentCDLCDetails,
                timestamp = DateTime.Now,
                completed = completed,
                paused = paused,
                arrangementID = currentSongRunArrangementID,
                path = currentSongRunPath,
                tuning = currentSongRunTuning,
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode,
                readout = readout
            };
        }

        private void ResetSongRun()
        {
            // The end guard stays set until the next start, so the same run can't end twice
            songRunStartedForID = null;
            currentSongRunArrangementID = null;
            currentSongRunPath = null;
            currentSongRunTuning = null;
            currentSongRunWasNonstopMode = false;
            currentSongRunWasMultiplayerMode = false;
        }

        private void ResetRunTiming()
        {
            lowTime = float.MaxValue;
            initTime = float.MaxValue;
            maxTime = float.MinValue;
            pauseTimerSnapshot = float.MinValue;
            paused = false;
        }

        /// <summary>
        /// Update the state of the sniffer
        /// </summary>
        private void UpdateState()
        {
            //Super complex state machine of state transitions
            switch (currentState)
            {
                case SnifferState.IN_MENUS:
                    // songTimer can read non-zero in menus, e.g. right after attaching
                    if (currentMemoryReadout.songTimer != 0 &&
                        IsPotentiallyPlayingGameStage(currentMemoryReadout.gameStage))
                    {
                        currentState = SnifferState.SONG_SELECTED;
                    }
                    break;
                case SnifferState.SONG_SELECTED:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        currentState = SnifferState.SONG_STARTING;
                    }

                    //If we somehow missed some states, skip to SONG_PLAYING
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        BeginSongRun();
                    }
                    break;
                case SnifferState.SONG_STARTING:
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        BeginSongRun();
                    }
                    // Backed out while loading, the timer never moved
                    else if (IsDefinitelyMenuGameStage(currentMemoryReadout.gameStage))
                    {
                        currentState = SnifferState.IN_MENUS;
                    }
                    break;
                case SnifferState.SONG_PLAYING:
                    if (currentCDLCDetails != null &&
                        currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - 0.201f)
                    {
                        currentState = SnifferState.SONG_ENDING;
                    }

                    //If the timer goes to 0 before the end, the song was quit, failed or the poll missed SONG_ENDING
                    if (currentMemoryReadout.songTimer == 0 &&
                        initTime != float.MaxValue)
                    {
                        EndSongRun(DetermineCompletedForForceEnd());
                        currentState = SnifferState.IN_MENUS;
                        ResetRunTiming();
                        break;
                    }

                    if (previousPauseMenuMode == PauseMenuMode.None &&
                        currentMemoryReadout.isPaused &&
                        initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PAUSED;
                        paused = true;
                        pauseTimerSnapshot = currentMemoryReadout.songTimer;
                    }
                    break;
                case SnifferState.SONG_PAUSED:
                    // Timer back at the start: quit or restart
                    if (currentMemoryReadout.songTimer <= initTime &&
                        initTime != float.MaxValue)
                    {
                        currentState = SnifferState.IN_MENUS;
                        EndSongRun(completed: false);
                        ResetRunTiming();
                    }
                    // Overlay closed and the timer moved: resume, or a restart that the
                    // branch above missed. A restart resets the note count, a resume doesn't.
                    else if (!currentMemoryReadout.isPaused &&
                             currentMemoryReadout.songTimer > initTime &&
                             currentMemoryReadout.songTimer != pauseTimerSnapshot)
                    {
                        int currentTotal = currentMemoryReadout.noteData?.TotalNotes ?? -1;
                        int snapshotTotal = lastInRunReadout?.noteData?.TotalNotes ?? -1;
                        if (currentTotal >= 0 && snapshotTotal > 0 && currentTotal < snapshotTotal)
                        {
                            EndSongRun(completed: false);
                            currentState = SnifferState.IN_MENUS;
                            ResetRunTiming();
                        }
                        else
                        {
                            currentState = SnifferState.SONG_PLAYING;
                            pauseTimerSnapshot = float.MinValue;
                        }
                    }
                    break;
                case SnifferState.SONG_ENDING:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        EndSongRun(completed: true);
                        currentState = SnifferState.IN_MENUS;
                        ResetRunTiming();
                    }
                    break;
                default:
                    break;
            }

            //Force state to IN_MENUS if the current song details are not valid
            if (!currentCDLCDetails.IsValid() &&
                currentState != SnifferState.IN_MENUS &&
                currentState != SnifferState.SONG_ENDING &&
                currentState != SnifferState.SONG_PAUSED)
            {
                currentState = SnifferState.IN_MENUS;
            }

            //If state changed
            if (currentState != previousState)
            {
                //Invoke event
                OnStateChanged?.Invoke(this, new OnStateChangedArgs() { oldState = previousState, newState = currentState });

                //Remember previous state
                previousState = currentState;

                if (Logger.logStateMachine)
                {
                    Logger.Log("Current state: {0}", currentState.ToString());
                }
            }
        }

        /// <summary>
        /// In a song, or possibly: *_pause stays set after resuming
        /// </summary>
        private static bool IsPotentiallyPlayingGameStage(string gameStage)
        {
            switch (gameStage)
            {
                case "las_game":
                case "sa_game":
                case "nonstopplaygame":
                case "las_pause":
                case "sa_pause":
                case "nsp_pause":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Known menu, tuner and song review stages. Unknown stages return false.
        /// </summary>
        private static bool IsDefinitelyMenuGameStage(string gameStage)
        {
            switch (gameStage)
            {
                case "mainmenu":
                case "gcpre":
                case "learnasong":
                case "scoreattack":
                case "nonstopplay":
                case "nsp_main":
                case "las_songs":
                case "las_options":
                case "las_tuner":
                case "nsp_tuner":
                case "las_songreview":
                case "sa_songreview":
                case "panel_bib":
                case "shop":
                case "gcade":
                    return true;
                default:
                    return false;
            }
        }
    }
}
