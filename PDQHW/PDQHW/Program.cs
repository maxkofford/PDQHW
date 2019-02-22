using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


/*
    This project was written for PDQ as a hw for application.
    Writen by Max Kofford - maxkofford@gmail.com

    Im assuming a couple things while writing this because a couple bits were unclear

1. The second argument, the file pattern, Is this the file pattern used in the C# Directory.GetFiles or is this a regular expression that I need to parse?

    -im assuming it is using the Directory.GetFiles pattern for simplicity

2. For output to the console,  is it exactly as specified in the description? For example: for a newly created file called "hello.txt" with a length of 4 lines do i print "hello.txt4" or am i supposed to put spaces or identifications such as "Name:" or "Length:" in the line?  Also for a file that has been modified but has the same number of lines what is it supposed to output (name+0  or name-0 or just name)?

    -im assuming that it is not exactly as specified and im putting spaces in the output between names and numbers
    -im also going with +0 for files that change but have the same line length

3. If in the case of large or locked files, does order of the output matter?  If a large file is created and started to be scanned, a second smaller file may be created within the next 10 seconds and get scanned before the larger file and therefore will get printed to the output before the earlier created large file.

    -im assuming order of output does not matter given that threads may always screw up order of output and trying to keep order of output eliminates the purpose of the threads (to prevent holdups)

4.If a file already exists in the directory and is also locked prior to starting the program, Should there be a creation message when it gets unlocked even though it existed before starting the program?

    -im going with yes given that i cant really scan the file until it gets unlocked anyways

5.If a file exists prior to starting the program should there be any initial creation messages for initial detection?

    -im going to assume no because these files already existed and are therefore not a file creation event (except for the previously mentioned locked files)
   
*/

namespace PDQHW
{
    class Program
    {
        //little ease of testing code
        //writing actual comprehensive tests would involve much file creation and deletion which is simpler to run by hand
        //also the hw specifications did not call for including testing code
        static void Testing()
        {
            string path = @".";
            string filePattern = "*.txt";
            Console.WriteLine(Path.GetDirectoryName(path));

            TextFileWatcher watcher = new TextFileWatcher(path, filePattern);
            watcher.StartTextFileWatch();
        }


        static void Main(string[] args)
        {
            
            if (args.Length < 2)
                return;

            string path = args[0];
            string filePattern = args[1];

            TextFileWatcher watcher = new TextFileWatcher(path, filePattern);
            watcher.StartTextFileWatch();
            
        }

        //information stored for each file to calculate modification changes
        public class FileData
        {
            //old file timestamp
            public DateTime Time
            {
                get;
                set;
            }
            //old number of lines
            public long LineCount
            {
                get;
                set;
            }
        }

        public class TextFileWatcher
        {
            public string DirectoryPath
            {
                get;
                private set;
            }

            public string FilePattern
            {
                get;
                private set;
            }

            public TextFileWatcher(string path, string filePattern)
            {
                this.DirectoryPath = path;
                this.FilePattern = filePattern;
            }


            //I decided to go with threadsafe buildt in datastructures because they may be slower but speed wasnt a issue as far as i was aware and they are simpler and safer

            //How this keeps track of files
            //need a threadsafe option because it will be updated from other worker threads as they read files
            private ConcurrentDictionary<string, FileData> fileChangeTime = new ConcurrentDictionary<string, FileData>();

            //represents stuff currently being scanned to prevent things from being scanned multiple times for large files
            //large enough files will try to shove themselves down the pipeline multiple times cause they so slow
            private ConcurrentDictionary<string, byte> currentlyScanningFiles = new ConcurrentDictionary<string, byte>();
            public void StartTextFileWatch()
            {

                //find every file that matches the pattern in that directory

                string[] startingFiles = GetDirectoryFiles();

                //initial files that exist prior to the program are counted as 
                foreach (string oneFile in startingFiles)
                {

                    string threadFileName = oneFile;
                    if (!currentlyScanningFiles.ContainsKey(threadFileName))
                    {
                        currentlyScanningFiles.TryAdd(threadFileName, 0);
                        ThreadPool.QueueUserWorkItem(WorkerThreadCreation, new Tuple<string, bool>(threadFileName, false));
                    }

                }



                //loop every 10 seconds

                while (true)
                {
                    Thread.Sleep(10000);

                    string[] currentFiles = GetDirectoryFiles();

                    //if a file is deleted output its name and delete it from the files being kept track of
                    {
                        List<string> filesThatHaveBeenDeleted = new List<string>();
                        foreach (var oldFile in fileChangeTime)
                        {
                            if (!currentFiles.Contains(oldFile.Key))
                            {
                                Console.WriteLine(Path.GetFileName(oldFile.Key));
                                filesThatHaveBeenDeleted.Add(oldFile.Key);
                            }
                        }

                        //cant delete from the dictionary while inside a iterator for the dictionary, so deletion gets handled out here
                        foreach (string deletedFile in filesThatHaveBeenDeleted)
                        {
                            fileChangeTime.TryRemove(deletedFile, out _);
                        }
                    }



                    foreach (string oneFile in currentFiles)
                    {
                        //if any new files found print its name and line count and add it to the files that being kept track of
                        if (!fileChangeTime.ContainsKey(oneFile))
                        {
                            string threadFileName = oneFile;
                            if (!currentlyScanningFiles.ContainsKey(threadFileName))
                            {
                                currentlyScanningFiles.TryAdd(threadFileName, 0);
                                ThreadPool.QueueUserWorkItem(WorkerThreadCreation, new Tuple<string, bool>(threadFileName, true));
                            }
                        }
                        else
                        {
                            //if any files are modified print its name and changed line count and modify their information being kept track of
                            string threadFileName = oneFile;
                            DateTime newTime = File.GetLastWriteTime(threadFileName);

                            if (!fileChangeTime[threadFileName].Time.Equals(newTime))
                            {
                                if (!currentlyScanningFiles.ContainsKey(threadFileName))
                                {
                                    currentlyScanningFiles.TryAdd(threadFileName, 0);
                                    ThreadPool.QueueUserWorkItem(WorkerThreadModification, threadFileName);
                                }
                            }

                        }
                    }
                }
            }

            /// <summary>
            /// Gets the directory files and then converts them to lower case because files are case insensitive
            /// I believe this means changing a file name's casing is not supposed to result in messages
            /// </summary>
            private string[] GetDirectoryFiles()
            {
                string[] casedFiles = Directory.GetFiles(DirectoryPath, FilePattern);
                for (int index = 0; index < casedFiles.Length; index++)
                {
                    casedFiles[index] = casedFiles[index].ToLower();
                }
                return casedFiles;
            }

            /// <summary>
            /// Calculates the amount of lines in a file
            /// Returns a nullable type because sometimes when trying to read files they are locked so exceptions get thrown
            /// a null value represents a file that could not be read
            /// </summary>
            private long? GetLineCount(string path)
            {
                long totalCount = 0;
                bool exceptionFound = false;
                try
                {
                    using (var reader = File.OpenText(path))
                    {
                        while (reader.ReadLine() != null)
                        {
                            totalCount++;
                        }
                    }
                }
                //For locked files or other problems with the file we can just ignore them and try to rescan them on the next loop iteration
                catch
                {
                    exceptionFound = true;
                }

                if (exceptionFound)
                    return null;

                return totalCount;
            }

            /// <summary>
            /// The worker thread for a file that was detected as created
            /// It takes a tuple of a string path and a bool of whether or not to output to allow sharing this method with the initial adding code
            /// </summary>
            private void WorkerThreadCreation(object o)
            {
                Tuple<string, bool> data = (Tuple<string, bool>)o;
                string threadFileName = data.Item1;

                long? lineCount = GetLineCount(threadFileName);

                if (lineCount.HasValue)
                {
                    if (data.Item2)
                        Console.WriteLine(Path.GetFileName(threadFileName) + " " + lineCount);
                    fileChangeTime.TryAdd(threadFileName, new FileData() { Time = File.GetLastWriteTime(threadFileName), LineCount = lineCount.Value });
                }

                currentlyScanningFiles.TryRemove(threadFileName, out _);
            }

            /// <summary>
            /// The worker thread for a file that was detected as modified
            /// takes a string path
            /// </summary>
            private void WorkerThreadModification(object o)
            {
                string threadFileName = (string)o;
                DateTime newTime = File.GetLastWriteTime(threadFileName);


                long? newLineCount = GetLineCount(threadFileName);

                if (newLineCount.HasValue)
                {

                    Console.WriteLine(Path.GetFileName(threadFileName) + " " + (fileChangeTime[threadFileName].LineCount <= newLineCount ? "+" : "") + (newLineCount - fileChangeTime[threadFileName].LineCount));

                    fileChangeTime[threadFileName].LineCount = newLineCount.Value;
                    fileChangeTime[threadFileName].Time = newTime;


                }
 
                currentlyScanningFiles.TryRemove(threadFileName, out _);

            }
        }
    }
}
