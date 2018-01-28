using RockSnifferLib.Cache;
using RockSnifferLib.Events;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        /// Pattern to match to be a valid dlc file path
        /// </summary>
        private Regex dlcPSARCMatcher = new Regex(".*?dlc.*?\\.psarc$");

        /// <summary>
        /// Reference to the rocksmith process
        /// </summary>
        private Process rsProcess;

        /// <summary>
        /// Cache to use
        /// </summary>
        private ICache cache;

        /// <summary>
        /// The memory reader
        /// </summary>
        private RSMemoryReader memReader;

        /// <summary>
        /// Boolean to let async tasks finish
        /// </summary>
        private bool running = true;

        /// <summary>
        /// Instantiate a new Sniffer on process, using cache
        /// </summary>
        /// <param name="rsProcess"></param>
        /// <param name="cache"></param>
        public Sniffer(Process rsProcess, ICache cache)
        {
            this.rsProcess = rsProcess;
            this.cache = cache;

            memReader = new RSMemoryReader(rsProcess);

            DoMemoryReadout();
            DoStateMachine();
            DoSniffing();
        }

        private async void DoMemoryReadout()
        {
            while (running)
            {
                try
                {
                    //If we are in menus, search for HIRC pointers
                    if (currentState == SnifferState.IN_MENUS)
                    {
                        memReader.DoHIRCReadout();
                    }

                    //Read data from memory
                    currentMemoryReadout = memReader.DoReadout();
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while reading memory: {0} {1}", e.GetType(), e.Message);
                    }

                    //Silently ignore
                }

                OnMemoryReadout?.Invoke(this, new OnMemoryReadoutArgs() { memoryReadout = currentMemoryReadout });

                await Task.Delay(100);
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

                    //Silently ignore
                }

                //Delay for 100 milliseconds
                await Task.Delay(100);
            }
        }

        private async void DoSniffing()
        {
            while (running)
            {
                try
                {
                    //Sniff for song details
                    await Task.Run(() => Sniff());
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while sniffing: {0} {1}", e.GetType(), e.Message);
                    }

                    //Silently ignore
                }

                //Delay for 1 second
                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// Sniff file handles and update cdlc details
        /// <para></para>
        /// Will return the currently active song details even if not successful
        /// </summary>
        public SongDetails Sniff()
        {
            //Only sniff file handles if we are in the menus, or if the current song differs from the current memory readout
            if (currentState == SnifferState.IN_MENUS || (currentCDLCDetails.songID != currentMemoryReadout.songID))
            {
                string dlcFile = SniffFileHandles();

                if (dlcFile != null)
                {
                    UpdateCurrentDetails(dlcFile);
                }

                //If the song details are not valid, revalidate the HIRC pointer
                //Assuming that we got the wrong HIRC struct and the dlc filepath is correct
                if (!currentCDLCDetails.IsValid())
                {
                    memReader.RevalidateHIRC();
                }
            }

            return currentCDLCDetails;
        }

        /// <summary>
        /// Stops the sniffer, stopping all async tasks
        /// </summary>
        public void Stop()
        {
            running = false;
        }

        private string SniffFileHandles()
        {
            //Get all handles from the rocksmith process
            var handles = CustomAPI.GetHandles(rsProcess);

            //If there aren't any handles, return
            if (handles == null)
            {
                return null;
            }

            var strTemp = "";
            bool matched = false;
            var songsPsarc = "";

            //Go through all the handles
            for (int i = 0; i < handles.Count; i++)
            {
                //Read the filename from the file handle
                var fd = FileDetails.GetFileDetails(rsProcess.Handle, handles[i]);

                //If getting file details failed for this handle, skip it
                if (fd == null)
                {
                    continue;
                }

                strTemp = fd.Name;

                if (strTemp.EndsWith("songs.psarc"))
                {
                    songsPsarc = strTemp;
                }

                //Check if it matches the dlc pattern
                if (dlcPSARCMatcher.IsMatch(strTemp))
                {
                    //Mark that we found a DLC file
                    matched = true;

                    //Return this DLC file
                    return strTemp;
                }
            }

            if (!matched)
            {
                return songsPsarc;
            }

            return null;
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
                    if (currentMemoryReadout.songTimer != 0)
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
                    //Or if the user reset
                    if (currentMemoryReadout.songTimer > 1)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                    }
                    break;
                case SnifferState.SONG_STARTING:
                    if (currentMemoryReadout.songTimer > 0)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                    }
                    break;
                case SnifferState.SONG_PLAYING:
                    //Allow 5 seconds of error margin on song ending
                    if (currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - 5)
                    {
                        currentState = SnifferState.SONG_ENDING;
                    }
                    //If the timer goes to 0, the user must have quit
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        currentState = SnifferState.IN_MENUS;
                    }
                    break;
                case SnifferState.SONG_ENDING:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        currentState = SnifferState.IN_MENUS;
                    }
                    break;
                default:
                    break;
            }

            //Force state to IN_MENUS if the current song details are not valid
            if (!currentCDLCDetails.IsValid())
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

        private void UpdateCurrentDetails(string filepath)
        {
            //If the song has not changed, and the details object is valid, no need to update
            if (currentMemoryReadout.songID == currentCDLCDetails.songID && currentCDLCDetails.IsValid())
            {
                return;
            }

            //If songID is empty, we aren't gonna be able to do anything here
            if (currentMemoryReadout.songID == "")
            {
                return;
            }

            //Check if this psarc file is cached
            if (cache.Contains(filepath))
            {
                //Load from cache if it is
                currentCDLCDetails = cache.Load(filepath, currentMemoryReadout.songID);

                //If cache failed to load
                if (currentCDLCDetails == null)
                {
                    //Set invalid song details
                    currentCDLCDetails = new SongDetails();
                }

                //Print current details (if debug is enabled) and print warnings about this dlc
                currentCDLCDetails.print();

                //Invoke event
                OnSongChanged?.Invoke(this, new OnSongChangedArgs() { songDetails = currentCDLCDetails });

                //Exit function as data was handled from cache
                return;
            }

            //Read psarc data into the details object
            var allSongDetails = PSARCUtil.ReadPSARCHeaderData(filepath);

            //If loading failed
            if (allSongDetails == null)
            {
                //Exit function
                return;
            }

            //If this is the songs.psarc file, we should merge RS1 DLC into it
            if (filepath.EndsWith("songs.psarc"))
            {
                //Really ugly way to find the rs1 dlc psarc
                string rs1dlcpath = filepath.Replace("songs.psarc", "dlc" + System.IO.Path.DirectorySeparatorChar + "rs1compatibilitydlc_p.psarc");

                //Read the rs1 dlc psarc
                var rs1SongDetails = PSARCUtil.ReadPSARCHeaderData(rs1dlcpath);

                //If we got rs1 dlc arrangements
                if (rs1SongDetails != null)
                {
                    //Combine the two dictionaries
                    allSongDetails = allSongDetails.Concat(rs1SongDetails).ToDictionary(k => k.Key, v => v.Value);
                }
            }

            //If the song detail dictionary contains the song ID we are looking for
            if (allSongDetails.ContainsKey(currentMemoryReadout.songID))
            {
                //Assign current CDLC details
                currentCDLCDetails = allSongDetails[currentMemoryReadout.songID];
            }

            //Add this CDLC file to the cache
            cache.Add(filepath, allSongDetails);

            //Print current details (if debug is enabled) and print warnings about this dlc
            currentCDLCDetails.print();

            //Invoke event
            OnSongChanged?.Invoke(this, new OnSongChangedArgs() { songDetails = currentCDLCDetails });

            return;
        }
    }
}
