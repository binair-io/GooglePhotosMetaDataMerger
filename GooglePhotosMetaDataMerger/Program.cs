using System;
using System.IO;
using System.Linq;
using CommandLine;
using System.Text.Json;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp;

namespace GooglePhotosMetaDataMerger
{
    class Program
    {
        private static bool _verbose;
        private static string _outputRootFolder;
        private static string _folder;

        public class Options
        {
            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }

            [Option('f', "folder", Required = true, HelpText = "Set folder to scan files recursively.")]
            public string Folder { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       _verbose = true; // o.Verbose;
                       _folder = o.Folder;
                       _outputRootFolder = Path.GetFullPath(o.Folder) + "_merged";

                       TraverseFolder(o.Folder);
                   });

            if (_verbose) Console.WriteLine("Finished!");
            Console.ReadLine();
        }

        private static void TraverseFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException(nameof(folder));
            if (!System.IO.Directory.Exists(folder)) throw new ArgumentException($"Folder not found '{folder}'");
            if (_verbose) Console.WriteLine($"Traversing folder {folder}");

            // Create mirrored directory structure        
            Directory.CreateDirectory(folder.Replace(_folder, _outputRootFolder));

            //  Loop all nested folders recursively
            foreach (var childFolder in Directory.GetDirectories(folder))
            {
                TraverseFolder(childFolder);
            }

            // Loop all files in folder that are not json files
            foreach (var filePath in Directory.GetFiles(folder).Where(x => !x.EndsWith(".json")))
            {
                if (_verbose) Console.WriteLine(filePath);
                MapFileMetaData(filePath);
            }
        }

        private static void MapFileMetaData(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists($"{filePath}.json"))
            {
                if (_verbose) Console.WriteLine($"Cannot find metadata file for {filePath}, skipping merge.");
                return;
            }

            if(File.Exists(filePath.Replace(_folder, _outputRootFolder)))
            {
                if(_verbose) Console.WriteLine($"File {filePath} already processed, skipping merge.");
                return;
            }

            // Open the file, create ImageSharp Image and load metadata for the file
            using (var imageInBytes = File.OpenRead(filePath))
            using (var image = Image.Load(imageInBytes, out var imageFormat))
            using (var metadata = JsonDocument.Parse(File.ReadAllBytes($"{filePath}.json")))
            {
                // Format for Exif data
                const string dateFormat = "yyyy:MM:dd HH:mm:ss";

                // Root of metadata file matching current file
                var metadataRoot = metadata.RootElement;
                // Get image exif profile
                var exifProfile = image.Metadata.ExifProfile ?? new ExifProfile();

                if (int.TryParse(metadataRoot.GetProperty("photoTakenTime").GetProperty("timestamp").GetString(), out int creationTimestamp))
                {
                    // Google Photos timestamp is java based so seconds need to be added to this date
                    var javaStartDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);

                    // Add seconds to base date and format to exif date format
                    var metaDateTimeOriginal = javaStartDateTime.AddSeconds(creationTimestamp);
                    var dt = metaDateTimeOriginal.ToString(dateFormat);

                    // Set exif value
                    exifProfile.SetValue(ExifTag.DateTimeOriginal, dt);

                    if (_verbose) Console.WriteLine($"DateTimeOriginal: {dt.ToString()}");
                }

                // Set exif profile
                image.Metadata.ExifProfile = exifProfile;

                // Write file to mirrored folder structure
                image.Save(filePath.Replace(_folder, _outputRootFolder));
            }
        }
    }
}
