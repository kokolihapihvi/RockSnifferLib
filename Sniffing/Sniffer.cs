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
using System.Text;
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
        public event EventHandler<OnActualSongStartArgs> OnActualSongStart;
        public event EventHandler<OnActualSongEndArgs> OnActualSongEnd;
        public event EventHandler<OnMemoryReadoutArgs> OnMemoryReadout;

        /// <summary>
        /// Fired when a song starts
        /// </summary>
        public event EventHandler<OnSongStartedArgs> OnSongStarted;

        /// <summary>
        /// Fired when a song ends
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

        /// <summary>
        /// Timer tracking for pause detection / game stage
        /// </summary>
        private float lowTime = float.MaxValue;
        private float initTime = float.MaxValue;
        private float maxTime = float.MinValue;
        private bool paused = false;
        private bool completed = false;

        /// <summary>
        /// Stall-counter pause detection: counts consecutive memory reads
        /// where the song timer has not meaningfully advanced.
        /// </summary>
        private float lastObservedTimer = float.MinValue;
        // lastObservedTimer is retained for diagnostic logging only; pause detection
        // is flag-driven via RSMemoryReadout.pauseMenuMode.

        /// <summary>
        /// Snapshot of songTimer at the moment SONG_PAUSED was entered. Distinguishes
        /// resume (timer moves) from Exit-from-pause (pauseMenuMode flips to 0 while
        /// the timer stays frozen at the pause point) - without it, Exit produces a
        /// spurious SONG_PAUSED -> SONG_PLAYING flicker before the menu transition lands.
        /// </summary>
        private float pauseTimerSnapshot = float.MinValue;

        /// <summary>
        /// Previous poll's pauseMenuMode. Pause entry requires the None -> non-None
        /// TRANSITION, not the raw value - after Restart from the pause menu, Rocksmith
        /// briefly keeps reading TopOverlay while the new song is already playing, which
        /// would otherwise fire a false SONG_PLAYING -> SONG_PAUSED. Captured each poll
        /// before newReadout overwrites currentMemoryReadout.
        /// </summary>
        private PauseMenuMode previousPauseMenuMode = PauseMenuMode.None;

        // SONG-RUN CONTEXT: the arrangement context (ID, path, tuning) of the running
        // song, captured at LogSongStartIfPossible and preserved through LogSongEnd.
        // In Nonstop Play the songID can flip to the next song before the current
        // song's end fires, and the cross-reference then nulls
        // currentMemoryReadout.arrangementID - reading from the readout at end-time
        // would lose the context.
        private string currentSongRunArrangementID = null;
        private string currentSongRunPath = null;
        private string currentSongRunTuning = null;

        // Rolling snapshot of the readout while a run is live (in-run state, timer
        // advanced). END payloads read from this instead of currentMemoryReadout:
        // on Restart-from-pause, Rocksmith zeroes the in-memory noteData for the
        // NEW attempt before the old attempt's force-end fires, so the current
        // readout at end-time reports TotalNotes=0 / Accuracy=100 (0-of-0). The
        // snapshot still holds the old attempt's last real values. Reset at song
        // start so an instantly-abandoned run can't inherit the previous song's
        // stats.
        private RSMemoryReadout lastInRunReadout = null;
        // True if the song started in a Nonstop Play gameStage (nsp_main /
        // nonstopplayhub / nonstopplaygame). Set by Sniffer.cs at song start.
        // Informational only.
        private bool currentSongRunWasNonstopMode = false;

        // True if the current song run started in a Multiplayer gameStage (split_game,
        // mp_*, duet_*, h2h_*). PlaythroughHistory and the JS playthrough-tracker use
        // it to skip writes: multi-user note data isn't tracked.
        private bool currentSongRunWasMultiplayerMode = false;

        // Fire-once guards: the natural state-machine path and the gameStage /
        // songID-change escape hatches may BOTH try to fire START / END; these track
        // the songID last fired for, so each event fires at most once per song run.
        // Reset on songID change so a replay produces a new start/end pair.
        private string lastLogStartedForSongID = null;
        private string lastLogEndedForSongID = null;

        // Floor for case-2 (no-input-boot) detection. Rocksmith's no-input
        // abort only fires before the timer advances meaningfully; once
        // playback is moving, audio-input drop triggers auto-pause instead.
        // 0.1s is well below any legitimate play time and well above the
        // float-near-zero initialization flashes (e.g. 9.2515e-37) that can
        // briefly appear during chart load.
        private const float MIN_PROGRESS_SECONDS = 0.1f;

        // Previous gameStage observed (used to detect transitions, primarily for
        // Nonstop Play where the timer-based state machine is unreliable).
        private string lastGameStage = null;


        // Public properties to expose completed and paused status
        public bool Completed => completed;
        public bool Paused => paused;

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
                OnSongStarted?.Invoke(this, new OnSongStartedArgs { song = currentCDLCDetails });
            }
            else if (newState == SnifferState.IN_MENUS &&
                oldState != SnifferState.NONE)
            {
                OnSongEnded?.Invoke(this, new OnSongEndedArgs { song = currentCDLCDetails, completed = completed, paused = paused });
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
                    // Force-end the outgoing song: if START fired for it but END never did, fire
                    // END now, BEFORE currentCDLCDetails updates to the new song. Primarily for
                    // Nonstop Play, where the timer-based state machine can park in SONG_ENDING
                    // and never naturally end.
                    if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                        lastLogStartedForSongID != null &&
                        lastLogStartedForSongID == currentCDLCDetails.songID &&
                        lastLogEndedForSongID != currentCDLCDetails.songID)
                    {
                        LogSongEnd(DetermineCompletedForForceEnd());

                        // Reset state machine and timing for the upcoming new song
                        currentState = SnifferState.IN_MENUS;
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                    }

                    var newDetails = _cache.Get(newReadout.songID);

                    if (newDetails != null && newDetails.IsValid())
                    {
                        currentCDLCDetails = _cache.Get(newReadout.songID);
                        OnSongChanged?.Invoke(this, new OnSongChangedArgs { songDetails = currentCDLCDetails });
                        currentCDLCDetails.Print();

                        // Reset pause / timing state on song change
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;

                        // Reset song-run context and fire-once guards for the new song
                        currentSongRunArrangementID = null;
                        currentSongRunPath = null;
                        currentSongRunTuning = null;
                        currentSongRunWasNonstopMode = false;
                        currentSongRunWasMultiplayerMode = false;
                        lastLogStartedForSongID = null;
                        lastLogEndedForSongID = null;
                    }

                }

                // Arrangement-ID cross-reference: the memory read can return a STALE but
                // format-valid hash from the previous song after the songID has flipped
                // (especially in Nonstop). Format validation can't catch it - the value is a
                // real 32-char hex hash for the wrong song - so null arrangementID whenever it
                // matches no arrangement of the current song.
                if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                    !string.IsNullOrEmpty(newReadout.arrangementID))
                {
                    bool matchesAnArrangement = false;
                    foreach (var arr in currentCDLCDetails.arrangements)
                    {
                        if (arr.arrangementID == newReadout.arrangementID)
                        {
                            matchesAnArrangement = true;
                            break;
                        }
                    }
                    if (!matchesAnArrangement)
                    {
                        newReadout.arrangementID = null;
                    }
                }

                // Capture previous poll's pauseMenuMode BEFORE CopyTo overwrites
                // currentMemoryReadout; pause entry requires a real 0 -> non-zero transition.
                previousPauseMenuMode = currentMemoryReadout?.pauseMenuMode ?? PauseMenuMode.None;

                newReadout.CopyTo(ref currentMemoryReadout);

                // Rolling in-run snapshot for END payloads (see lastInRunReadout docs).
                // Two guards beyond the state check:
                //   timer > 0: the timer-reset poll that triggers a force-end must
                //   not capture.
                //   monotonic timer: after Restart-from-pause, gameStage/pauseMenuMode
                //   stickiness keeps the state reading SONG_PAUSED while the NEW
                //   attempt's timer is already climbing; without this guard the new
                //   attempt's zeroed noteData overwrites the old attempt's snapshot
                //   before its force-end fires. A backwards timer always means a new
                //   attempt (restart) or the post-resume rewind; in both cases the
                //   existing snapshot is the one END must keep. Captures resume once
                //   the timer passes the snapshot again (resume case) or the START
                //   baseline resets the snapshot (restart case).
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

                // Track timer behaviour for pause detection
                if (currentMemoryReadout.songTimer >= 0.001f)
                {
                    // Set initTime to the first valid timer value, plus one polling interval buffer
                    if (lowTime == float.MaxValue || currentMemoryReadout.songTimer < lowTime)
                    {
                        lowTime = currentMemoryReadout.songTimer;
                        initTime = currentMemoryReadout.songTimer + 0.101f; // ~100ms polling interval + offset for safe restart detection
                    }

                    // Update max observed timer
                    maxTime = Math.Max(maxTime, currentMemoryReadout.songTimer);

                    // lastObservedTimer is diagnostic-only; no state-machine logic branches on it.
                    lastObservedTimer = currentMemoryReadout.songTimer;
                }

                // Game-stage transition detection - primarily for Nonstop Play, where the
                // timer-based state machine can park in SONG_ENDING between songs (it only
                // naturally exits on songTimer == 0). gameStage is the direct signal of what
                // Rocksmith is doing; the transitions below force-fire START / END where the
                // natural path is unreliable.
                string currentGameStage = currentMemoryReadout.gameStage;
                if (currentGameStage != lastGameStage)
                {
                    string prevStage = lastGameStage;
                    string newStage = currentGameStage;

                    // nonstopplaygame -> nonstopplayhub: current song just ended.
                    // Force-fire LogSongEnd if we have a started-but-not-ended song.
                    // Note: typically the songID-change force-end (above) catches this
                    // first; this is a backstop for when the gameStage transitions
                    // before the songID changes.
                    if (prevStage == "nonstopplaygame" && newStage == "nonstopplayhub")
                    {
                        if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                            lastLogStartedForSongID != null &&
                            lastLogStartedForSongID == currentCDLCDetails.songID &&
                            lastLogEndedForSongID != currentCDLCDetails.songID)
                        {
                            LogSongEnd(DetermineCompletedForForceEnd());

                            currentState = SnifferState.IN_MENUS;
                            lowTime = float.MaxValue;
                            initTime = float.MaxValue;
                            maxTime = float.MinValue;
                                lastObservedTimer = float.MinValue;
                                pauseTimerSnapshot = float.MinValue;
                            paused = false;
                        }
                    }
                    // nonstopplayhub -> nonstopplaygame: new song is now playing; force-fire START.
                    // initTime guard: songTimer briefly flashes nonzero during loading screens, so
                    // only fire once the timer has actually advanced past initTime.
                    else if (prevStage == "nonstopplayhub" && newStage == "nonstopplaygame")
                    {
                        if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                            lastLogStartedForSongID != currentCDLCDetails.songID &&
                            initTime != float.MaxValue &&
                            currentMemoryReadout.songTimer > initTime)
                        {
                            LogSongStartIfPossible();
                            // We're definitely in-game now; advance state machine accordingly.
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
            foreach (var dir in dirs)
            {
                // Check if path has the reparsepoint attribute (it is most likely a symlink)
                if (new FileInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
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
        /// Update the state of the sniffer
        /// </summary>
        /// 
        private void LogSongStartIfPossible()
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return;
            }

            // Fire-once guard: don't log start twice for the same song run.
            // Important when both the natural state machine AND the gameStage-transition
            // escape hatch try to fire start for the same song.
            if (lastLogStartedForSongID != null &&
                lastLogStartedForSongID == currentCDLCDetails.songID)
            {
                return;
            }

            // STEP 1: Direct arrangementID match - exact, best resolution.
            var arrangement = currentCDLCDetails.arrangements?
                .FirstOrDefault(a => a.arrangementID == currentMemoryReadout.arrangementID);

            string fallbackReason = null;

            // STEP 2: Path filter. If the direct match failed, use the user's
            // currently-selected Path (stable menu-level byte - see
            // MemoryOffsets.GetCurrentPathPointer; reliable from launch and valid in
            // Nonstop Play). Path only encodes the type (Lead/Rhythm/Bass), so filter
            // non-bonus/non-alternate first to disambiguate.
            string currentPath = null;
            if (currentMemoryReadout != null && currentMemoryReadout.currentPath != RSPath.Unknown)
            {
                currentPath = currentMemoryReadout.currentPath.ToString();
            }
            if (arrangement == null && !string.IsNullOrEmpty(currentPath) &&
                currentCDLCDetails.arrangements != null)
            {
                var arrangements = currentCDLCDetails.arrangements;

                // 2a: Prefer non-bonus, non-alternate - first match wins.
                foreach (var arr in arrangements)
                {
                    if ((arr.type == currentPath || arr.name == currentPath) &&
                        !arr.isBonusArrangement && !arr.isAlternateArrangement)
                    {
                        arrangement = arr;
                        fallbackReason = $"current Path \"{currentPath}\" + non-bonus filter";
                        break;
                    }
                }

                // 2b: Bonus/alt allowed if no regular match - first match wins
                if (arrangement == null)
                {
                    foreach (var arr in arrangements)
                    {
                        if (arr.type == currentPath || arr.name == currentPath)
                        {
                            arrangement = arr;
                            fallbackReason = $"current Path \"{currentPath}\" (bonus/alt allowed)";
                            break;
                        }
                    }
                }
            }

            // STEP 3+: defensive fallback chain, reached only when both the direct match
            // and Path resolution failed (e.g. transient memory hiccup).
            if (arrangement == null)
            {
                var arrangements = currentCDLCDetails.arrangements;

                if (arrangements != null && arrangements.Count > 0)
                {
                    // STEP 3: Single-playable-arrangement heuristic
                    ArrangementDetails singlePlayable = null;
                    int playableCount = 0;
                    foreach (var arr in arrangements)
                    {
                        if (!arr.isBonusArrangement && !arr.isAlternateArrangement)
                        {
                            singlePlayable = arr;
                            playableCount++;
                            if (playableCount > 1) break; // can stop early - already ambiguous
                        }
                    }

                    if (playableCount == 1)
                    {
                        arrangement = singlePlayable;
                        fallbackReason = "single-playable-arrangement heuristic";
                    }

                    if (arrangement == null && arrangements.Count == 1)
                    {
                        // STEP 4: only-arrangement-on-song (last resort, even bonus/alternate)
                        arrangement = arrangements[0];
                        fallbackReason = "only-arrangement-on-song heuristic";
                    }
                }
            }


            // Capture the Nonstop flag at song START (gameStage may transition by end).
            // Informational on event args; also suppresses the "Could not resolve
            // arrangement" warning for Nonstop runs - see the warning site.
            string startGameStage = currentMemoryReadout?.gameStage;
            currentSongRunWasNonstopMode =
                startGameStage == "nsp_main" ||
                startGameStage == "nonstopplayhub" ||
                startGameStage == "nonstopplaygame";

            // Capture the Multiplayer flag at song START - PlaythroughHistory and the JS
            // tracker use it to skip writes. Uses the gameStage-derived mode field (the
            // RSMode classifier already covers split_game / mp_* / duet_* / h2h_*).
            currentSongRunWasMultiplayerMode = currentMemoryReadout?.mode == RSMode.MULTIPLAYER;

            string path;
            string tuning;

            if (arrangement != null)
            {
                path = arrangement.type;
                tuning = arrangement.tuning.TuningName;

                if (fallbackReason != null && !currentSongRunWasNonstopMode)
                {
                    // Suppress the warning for Nonstop runs: brief transients during song-to-song
                    // transitions can momentarily fail resolution and fall back to Path; logging
                    // those as errors would be noise on every Nonstop song.
                    Logger.LogError(
                        "Could not resolve arrangement at song start (memory arrangementID was '{0}'). Used fallback ({1}) and chose path='{2}', tuning='{3}'. Song will be logged to history with these values.",
                        currentMemoryReadout.arrangementID ?? "<null>",
                        fallbackReason,
                        path,
                        tuning);
                }
            }
            else
            {
                // No usable arrangement and the heuristic couldn't disambiguate.
                // Log the song anyway with explicit "unknown" markers so the row isn't silently dropped.
                path = "unknown";
                tuning = "unknown";
                int arrCount = currentCDLCDetails.arrangements?.Count ?? 0;
                Logger.LogError(
                    "Could not resolve arrangement at song start for song '{0}' (memory arrangementID was '{1}'). Song has {2} arrangements but none could be unambiguously selected; logging to history with path='unknown' / tuning='unknown'.",
                    currentCDLCDetails.songID ?? "<unknown>",
                    currentMemoryReadout.arrangementID ?? "<null>",
                    arrCount);
            }

            Logger.Log(
                $"EVENT=START;" +
                $"artist={currentCDLCDetails.artistName};" +
                $"album={currentCDLCDetails.albumName};" +
                $"year={currentCDLCDetails.albumYear};" +
                $"song={currentCDLCDetails.songName};" +
                $"length={currentCDLCDetails.songLength};" +
                $"path={path};" +
                $"tuning={tuning};" +
                $"author={(currentCDLCDetails.toolkit?.author ?? "").Trim()};"
            );

            // Capture song-run context so end-of-song logging can recover even if
            // currentMemoryReadout.arrangementID is later cleared (Nonstop transition).
            string resolvedArrangementID = arrangement?.arrangementID;
            currentSongRunArrangementID = resolvedArrangementID;
            currentSongRunPath = path;
            currentSongRunTuning = tuning;

            // Note: currentSongRunWasNonstopMode and startGameStage were already
            // computed above (before the warning block) so the warning could
            // suppress itself in Nonstop. No need to recompute here.

            // Fire-once guard: this songID's start is now logged.
            lastLogStartedForSongID = currentCDLCDetails.songID;

            // Baseline the in-run snapshot at run start so END can't inherit a
            // previous song's data if this run is abandoned before any capture.
            lastInRunReadout = currentMemoryReadout?.Clone();

            // Reset the END guard (cleared from any previous run of THIS or any other song).
            // Without this clear, if the user replays the same song (songID unchanged), the
            // LogSongEnd fire-once check would see lastLogEndedForSongID == currentCDLCDetails.songID
            // from the previous run and silently skip the new run's end-event firing.
            lastLogEndedForSongID = null;

            // Fire event with actual gameplay start timestamp
            var actualStartTimestamp = DateTime.Now;
            OnActualSongStart?.Invoke(this, new OnActualSongStartArgs
            {
                song = currentCDLCDetails,
                timestamp = actualStartTimestamp,
                arrangementID = resolvedArrangementID,
                path = path,
                tuning = tuning,
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode
            });
        }

        /// <summary>
        /// Decides the completed flag for force-end paths (SONG_PLAYING->timer=0,
        /// songID-change force-end, gameStage force-end). Returns true unless one
        /// of the three documented completed=false cases applies:
        ///   1. Pause-driven exit/restart/skip (currentState still SONG_PAUSED
        ///      when force-end fires - UpdateState() hasn't run yet this poll).
        ///   2. No-input boot at song start (timer never advanced past the
        ///      MIN_PROGRESS_SECONDS floor; Rocksmith aborts before playback).
        ///   3. Score Attack 3-strike fail (FailedPhrases >= 3, regardless of
        ///      how close maxTime got to songLength - last-phrase fails are
        ///      still fails).
        /// SA fail is checked FIRST so a last-phrase fail (where maxTime would
        /// otherwise look like a natural completion) still resolves to false.
        /// </summary>
        private bool DetermineCompletedForForceEnd()
        {
            // CASE 3: SA 3-strike fail
            if (currentMemoryReadout?.mode == RSMode.SCOREATTACK &&
                currentMemoryReadout.noteData is ScoreAttackNoteData saData &&
                saData.FailedPhrases >= 3)
            {
                return false;
            }

            // CASE 2: no-input boot at song start
            if (maxTime < MIN_PROGRESS_SECONDS)
            {
                return false;
            }

            // CASE 1: pause-driven exit/restart/skip. State is still SONG_PAUSED
            // when the force-end fires because pause-driven flows trigger the
            // force-end (via chart unload, songID change, or gameStage change)
            // before UpdateState() has a chance to transition the state machine.
            if (currentState == SnifferState.SONG_PAUSED)
            {
                return false;
            }

            // Otherwise: presumed natural completion (e.g. natural-end-of-song
            // that missed the SONG_ENDING transition due to poll cadence race,
            // or an NSP transition where the previous song completed naturally).
            return true;
        }

        private void LogSongEnd(bool completed)
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return;
            }

            // Don't fire end if start wasn't fired for this song run (e.g. user
            // quit during the deferral window before LogSongStart succeeded).
            // Without this guard, end events without paired start events could
            // produce orphan rows in playthrough_history.
            if (lastLogStartedForSongID != currentCDLCDetails.songID)
            {
                return;
            }

            // Fire-once guard: don't log end twice for the same song run.
            // Important when both the natural state machine AND the songID-change /
            // gameStage-transition escape hatches try to fire end for the same song.
            if (lastLogEndedForSongID != null &&
                lastLogEndedForSongID == currentCDLCDetails.songID)
            {
                return;
            }

            // Snapshot the song details and readout NOW, so any later updates to
            // currentCDLCDetails / currentMemoryReadout don't bleed into the event payload.
            var snapshotSong = currentCDLCDetails;
            // Prefer the rolling in-run snapshot: for natural completions its last
            // update IS the final state, and for force-ends (restart, songID flip,
            // gameStage) it holds the ended attempt's last real values rather than
            // whatever the memory reads after Rocksmith has already reset for the
            // next attempt.
            var snapshotReadout = (lastInRunReadout ?? currentMemoryReadout)?.Clone();
            var noteData = snapshotReadout?.noteData ?? currentMemoryReadout.noteData;

            // Build base log message
            StringBuilder logMessage = new StringBuilder();
            logMessage.Append($"EVENT=END;");
            logMessage.Append($"completed={completed};");
            logMessage.Append($"paused={paused};");
            logMessage.Append($"accuracy={Math.Round(noteData.Accuracy, 1)}%;");
            logMessage.Append($"totalNotes={noteData.TotalNotes};");
            logMessage.Append($"notesHit={noteData.TotalNotesHit};");
            logMessage.Append($"highestStreak={noteData.HighestHitStreak};");

            // Add Score Attack specific stats if in Score Attack mode
            if (snapshotReadout != null && snapshotReadout.mode == RSMode.SCOREATTACK && noteData is ScoreAttackNoteData saData)
            {
                logMessage.Append($"Mode=true;");
                logMessage.Append($"TotalPerfectHits={saData.TotalPerfectHits};");
                logMessage.Append($"PerfectPhrases={saData.PerfectPhrases};");
                logMessage.Append($"GoodPhrases={saData.GoodPhrases};");
                logMessage.Append($"PassedPhrases={saData.PassedPhrases};");
                logMessage.Append($"FailedPhrases={saData.FailedPhrases};");
                logMessage.Append($"HighestPerfectPhraseStreak={saData.HighestPerfectPhraseStreak};");
                logMessage.Append($"HighestGoodPhraseStreak={saData.HighestGoodPhraseStreak};");
                logMessage.Append($"HighestPassedPhraseStreak={saData.HighestPassedPhraseStreak};");
                logMessage.Append($"HighestFailedPhraseStreak={saData.HighestFailedPhraseStreak};");
                logMessage.Append($"CurrentScore={saData.CurrentScore};");
                logMessage.Append($"HighestMultiplier={saData.HighestMultiplier};");
            }

            Logger.Log(logMessage.ToString());

            // Mark this song's end as fired BEFORE invoking OnActualSongEnd
            // so re-entrant handlers (defensive) see the fire-once state.
            lastLogEndedForSongID = snapshotSong.songID;

            // Fire event with actual gameplay end timestamp.
            // Pass the song-run arrangement context (preserved from LogSongStart) and the
            // readout snapshot, so PlaythroughHistory can write the correct values even
            // if currentMemoryReadout / currentCDLCDetails have advanced to the next song.
            var actualEndTimestamp = DateTime.Now;
            OnActualSongEnd?.Invoke(this, new OnActualSongEndArgs
            {
                song = snapshotSong,
                timestamp = actualEndTimestamp,
                completed = completed,
                paused = paused,
                arrangementID = currentSongRunArrangementID,
                path = currentSongRunPath,
                tuning = currentSongRunTuning,
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode,
                readout = snapshotReadout
            });

            // Reset song-run state so a replay of the SAME song (songID unchanged: restart,
            // exit-and-replay, finish-and-replay) can fire start/end again as a NEW run.
            // We keep lastLogEndedForSongID set so any duplicate end-trigger paths in this
            // same poll cycle (e.g. songID-change AND gameStage-transition both trying to
            // force-end) get blocked; lastLogEndedForSongID is cleared on the next
            // successful LogSongStart.
            lastLogStartedForSongID = null;
            currentSongRunArrangementID = null;
            currentSongRunPath = null;
            currentSongRunTuning = null;
            currentSongRunWasNonstopMode = false;
            currentSongRunWasMultiplayerMode = false;
        }

        /// <summary>
        /// Update the state of the sniffer
        /// </summary>
        private void UpdateState()
        {
            // Super complex state machine of state transitions
            switch (currentState)
            {
                case SnifferState.IN_MENUS:
                    // Gate with gameStage: prevents spurious IN_MENUS -> SONG_SELECTED progression
                    // when attaching to a running process, on Rocksmith restart, or when transient
                    // memory garbage briefly reads a non-zero songTimer in a menu state.
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

                    // If we somehow missed some states, skip to SONG_PLAYING
                    // Using initTime instead of a hard-coded 1s threshold
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        LogSongStartIfPossible();
                    }
                    break;

                case SnifferState.SONG_STARTING:
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        LogSongStartIfPossible();
                    }
                    // Escape hatch: the user backed out of song-start before the timer advanced
                    // past initTime (e.g. Esc during loading). SONG_STARTING's only other exit is
                    // songTimer > initTime, so without this it parks indefinitely; gameStage
                    // returning to a menu value is the detector.
                    else if (IsDefinitelyMenuGameStage(currentMemoryReadout.gameStage))
                    {
                        Logger.Log("SONG_STARTING aborted (gameStage={0}, timer never advanced); returning to IN_MENUS", currentMemoryReadout.gameStage);
                        currentState = SnifferState.IN_MENUS;
                    }
                    break;

                case SnifferState.SONG_PLAYING:
                    // Allow small margin at end of song
                    if (currentCDLCDetails != null &&
                        currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - 0.201f)
                    {
                        currentState = SnifferState.SONG_ENDING;
                    }

                    // Timer hit 0 before the end: force-end. DetermineCompletedForForceEnd()
                    // decides the completed flag - catches SA 3-strike fail, no-input boot, and a
                    // natural completion whose SONG_ENDING window the poll missed.
                    if (currentMemoryReadout.songTimer == 0 &&
                        initTime != float.MaxValue)
                    {
                        completed = DetermineCompletedForForceEnd();

                        LogSongEnd(completed: completed);
                        currentState = SnifferState.IN_MENUS;

                        // Reset pause tracking for next run
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                        break;
                    }

                    // Pause entry: see the PauseMenuMode enum for the overlay-state values.
                    // Detection requires the None -> non-None TRANSITION - after Restart from
                    // the pause menu the value briefly stays TopOverlay while the new song
                    // plays, and raw-value detection would fire a false pause.
                    if (previousPauseMenuMode == PauseMenuMode.None &&
                        currentMemoryReadout.isPaused &&
                        initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PAUSED;
                        Logger.Log("Song Paused! (pauseMenuMode={0} at timer {1:F3})", currentMemoryReadout.pauseMenuMode, currentMemoryReadout.songTimer);
                        paused = true;
                        // Snapshot songTimer for resume-vs-exit disambiguation
                        // (see pauseTimerSnapshot field docs above for rationale).
                        pauseTimerSnapshot = currentMemoryReadout.songTimer;
                    }
                    break;

                case SnifferState.SONG_PAUSED:
                    // If the timer drops back to (or below) initTime, treat as restart / quit
                    if (currentMemoryReadout.songTimer <= initTime &&
                        initTime != float.MaxValue)
                    {
                        // Label restart vs quit-to-menu using the same note-counter
                        // discriminator as the timer-moved branch below: Restart zeroes
                        // noteData for the new attempt, a quit leaves the old count. The
                        // in-run snapshot holds the ended attempt's count to compare.
                        int currentTotal = currentMemoryReadout.noteData?.TotalNotes ?? -1;
                        int snapshotTotal = lastInRunReadout?.noteData?.TotalNotes ?? -1;
                        if (currentTotal >= 0 && snapshotTotal > 0 && currentTotal < snapshotTotal)
                        {
                            Logger.Log("Song Restarted! (notes counter reset {0} -> {1}, timer {2:F3})", snapshotTotal, currentTotal, currentMemoryReadout.songTimer);
                        }
                        else
                        {
                            Logger.Log("Song ended from pause (timer {0:F3} <= initTime {1:F3})", currentMemoryReadout.songTimer, initTime);
                        }

                        currentState = SnifferState.IN_MENUS;

                        // Not a full completion
                        completed = false;

                        LogSongEnd(completed: false);

                        // Reset timers so a new run gets clean values
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                    }
                    // Pause exit: flag-driven - resume is recognized on the first poll where
                    // pauseMenuMode returns to None AND songTimer has moved from the pause-entry
                    // snapshot. The timer guard matters: on Exit-from-pause the flag clears while
                    // the timer stays frozen, and without the guard that reads as a spurious
                    // resume before the menu transition lands. (Rocksmith also rewinds the timer
                    // slightly on resume, which the != comparison tolerates.)
                    else if (!currentMemoryReadout.isPaused &&
                             currentMemoryReadout.songTimer > initTime &&
                             currentMemoryReadout.songTimer != pauseTimerSnapshot)
                    {
                        // Restart-vs-resume disambiguation. Timer arithmetic alone cannot
                        // tell them apart: Restart seeks to the first-note point, resume
                        // rewinds ~2s from the pause point, and the two can land on the
                        // same timer value. The timer<=initTime branch above only catches
                        // restarts whose load transient (timer 0) happens to be polled
                        // while the pause flag is still sticky; quick restarts miss it.
                        // The reliable discriminator is the cumulative note counter:
                        // Restart zeroes noteData for the new attempt, resume preserves
                        // it. The in-run snapshot (monotonic, so the new attempt cannot
                        // have overwritten it) holds the old attempt's counter to compare
                        // against. When either side is unavailable, fall through to the
                        // resume interpretation (previous behavior).
                        int currentTotal = currentMemoryReadout.noteData?.TotalNotes ?? -1;
                        int snapshotTotal = lastInRunReadout?.noteData?.TotalNotes ?? -1;
                        if (currentTotal >= 0 && snapshotTotal > 0 && currentTotal < snapshotTotal)
                        {
                            Logger.Log("Song Restarted! (notes counter reset {0} -> {1}, timer {2:F3})", snapshotTotal, currentTotal, currentMemoryReadout.songTimer);

                            completed = false;
                            LogSongEnd(completed: false);
                            currentState = SnifferState.IN_MENUS;

                            // Reset timers so the new run gets clean values and START
                            // re-arms (same reset set as the timer<=initTime branch).
                            lowTime = float.MaxValue;
                            initTime = float.MaxValue;
                            maxTime = float.MinValue;
                            lastObservedTimer = float.MinValue;
                            pauseTimerSnapshot = float.MinValue;
                            paused = false;
                        }
                        else
                        {
                            currentState = SnifferState.SONG_PLAYING;
                            Logger.Log("Song Resumed! (pauseMenuMode=None at timer {0:F3}, was paused at {1:F3})", currentMemoryReadout.songTimer, pauseTimerSnapshot);
                            pauseTimerSnapshot = float.MinValue;
                        }
                    }
                    break;

                case SnifferState.SONG_ENDING:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        // Completed run

                        completed = true;

                        LogSongEnd(completed: true);
                        currentState = SnifferState.IN_MENUS;

                        // Reset pause / timing
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                    }
                    break;

                default:
                    break;
            }

            // Force state to IN_MENUS if the current song details are not valid
            if (!currentCDLCDetails.IsValid() &&
                currentState != SnifferState.IN_MENUS &&
                currentState != SnifferState.SONG_ENDING &&
                currentState != SnifferState.SONG_PAUSED)
            {
                currentState = SnifferState.IN_MENUS;
            }

            // If state changed, fire the event (this is what RockSniffer.exe / addons rely on)
            if (currentState != previousState)
            {
                OnStateChanged?.Invoke(this, new OnStateChangedArgs()
                {
                    oldState = previousState,
                    newState = currentState
                });

                previousState = currentState;

                if (Logger.logStateMachine)
                {
                    Logger.Log("Current state: {0}", currentState.ToString());
                }
            }
        }

        // gameStage classification helpers
        //
        // The startup transitions (IN_MENUS -> SONG_SELECTED, and the SONG_STARTING ->
        // IN_MENUS escape hatch) distinguish three categories of gameStage:
        //
        //   1. "Potentially playing": las_game, sa_game, nonstopplaygame, las_pause,
        //      sa_pause, nsp_pause. The user IS in a song, or MIGHT be - *_pause is
        //      sticky (Rocksmith only clears it on major stage transitions, not on
        //      resume from pause).
        //   2. "Definitely menu": mainmenu, gcpre, learnasong, scoreattack,
        //      nonstopplay, nsp_main, *_songs, *_options, *_tuner, *_songreview,
        //      panel_*, shop, gc_*, gcade, ge_*, mp_*, sm_*. Clearly not actively
        //      playing (includes tuners and post-song review).
        //   3. Unknown: treat as menu for entry guards (don't progress to
        //      SONG_SELECTED) but NOT for the SONG_STARTING escape (don't drop out on
        //      a possible uncatalogued song-start state).
        //
        // Exact-string comparison keeps classification deterministic; the safe default
        // for unknown stages is to make no state-machine decision from them.

        /// <summary>
        /// True if gameStage indicates the user is potentially in a song:
        /// either actively playing or in a sticky *_pause state.
        /// Used to gate IN_MENUS -> SONG_SELECTED transitions, preventing
        /// spurious progression based on transient memory garbage during
        /// Rocksmith startup, RockSniffer attach, or any menu navigation
        /// where songTimer briefly reads non-zero.
        /// </summary>
        private static bool IsPotentiallyPlayingGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage)) return false;
            switch (gameStage)
            {
                case "las_game":
                case "sa_game":
                case "nonstopplaygame":
                // *_pause stages included because gameStage doesn't reset on
                // resume: the user could be back in active gameplay with a
                // stale *_pause reading lingering until song-end / menu-nav.
                case "las_pause":
                case "sa_pause":
                case "nsp_pause":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True if gameStage is definitely a menu / tuner / songreview state,
        /// indicating the user is not actively playing a song.
        /// Used by the SONG_STARTING escape hatch to detect when the user
        /// has aborted song-start before the timer ever advanced.
        ///
        /// Deliberately excludes *_pause stages, which are sticky and may be
        /// lingering from a prior song's pause that hasn't been cleared by a
        /// major Rocksmith stage transition yet.
        ///
        /// Unknown stages return false (conservative default - better to stay
        /// in SONG_STARTING and let the timer-based path resolve than to
        /// bail out on an unrecognized value).
        /// </summary>
        private static bool IsDefinitelyMenuGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage)) return false;
            switch (gameStage)
            {
                // Top-level menus
                case "mainmenu":
                case "gcpre":
                case "learnasong":
                case "scoreattack":
                case "nonstopplay":
                case "nsp_main":
                // Per-mode menus (song select, options)
                case "las_songs":
                case "las_options":
                // Tuners (whether reached from a song-select menu or from a
                // pause menu, both treated as "not actively playing")
                case "las_tuner":
                case "nsp_tuner":
                // Song-end summary screens
                case "las_songreview":
                case "sa_songreview":
                // Other panels / menus
                case "panel_bib":
                case "shop":
                case "gcade":
                    return true;
                default:
                    // Unknown stages: don't bail out from SONG_STARTING. If a
                    // new "definitely menu" gameStage appears that's not on
                    // this list, it'll be added in a future release once
                    // we've observed and classified it.
                    return false;
            }
        }
    }
}