using System;
using System.IO;
using System.Linq;
using CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GooglePhotosMetaDataMerger
{
    class Program
    {
        private static string _folder;
        private static string _outputRootFolder;
        private static string _outputRootFolderBadProcess;
        private static ILogger<Program> _logger;

        public class Options
        {

            [Option('f', "folder", Required = true, HelpText = "Set folder to scan files recursively.")]
            public string Folder { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       _folder = o.Folder;
                       _outputRootFolder = Path.GetFullPath(o.Folder) + "_merged";
                       _outputRootFolderBadProcess = Path.GetFullPath(o.Folder) + "_bad";
                   });

            IHost host = Host.CreateDefaultBuilder(args).ConfigureServices(services =>
            {
                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddFile(Path.Combine(_outputRootFolder, "log.txt"), append: true);
                });
            }).Build();

            _logger = host.Services.GetRequiredService<ILogger<Program>>();
            _logger.LogInformation($"Begin processing: {DateTime.Now}");

            TraverseFolder(_folder);

            _logger.LogInformation($"Finished processing: {DateTime.Now}");
        }

        private static void TraverseFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException(nameof(folder));
            if (!System.IO.Directory.Exists(folder)) throw new ArgumentException($"Folder not found '{folder}'");

            _logger.LogDebug($"Traverse folder: {folder}");

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
                _logger.LogDebug($"MapFileMetaData: {filePath}");
                MapFileMetaData(filePath);
            }
        }

        private static void MapFileMetaData(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists($"{filePath}.json"))
            {
                _logger.LogWarning($"Cannot find metadata file for {filePath}, skipping merge.");
                return;
            }

            if (File.Exists(filePath.Replace(_folder, _outputRootFolder)))
            {
                _logger.LogDebug($"File {filePath} already processed, skipping merge.");
                return;
            }

            using (var metadata = JsonDocument.Parse(File.ReadAllBytes($"{filePath}.json")))
            {
                // Root of metadata file matching current file
                var metadataRoot = metadata.RootElement;
                // Google Photos timestamp is java based so seconds need to be added to this date
                var javaStartDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);

                if (int.TryParse(metadataRoot.GetProperty("photoTakenTime").GetProperty("timestamp").GetString(), out int creationTimestamp))
                {
                    // Add seconds to base date and format to exif date format
                    var metaDateTimeOriginal = javaStartDateTime.AddSeconds(creationTimestamp);
                    TagLib.File tfile;

                    try
                    {
                        // Instantiate taglib file
                        tfile = TagLib.File.Create(filePath);

                        if (tfile is TagLib.Image.File)
                        {
                            var itag = (tfile as TagLib.Image.File).Tag as TagLib.Image.CombinedImageTag;
                            itag.DateTime = metaDateTimeOriginal;
                        }
                        else
                        {
                            _logger.LogWarning($"File: {filePath} :  Implement MimeType: {tfile.MimeType}");
                        }
                        tfile.Save();
                        File.Copy(filePath, filePath.Replace(_folder, _outputRootFolder));
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning($"File: {filePath} : InvalidOperation: {ex.Message}");
                        CopyFaultyFiles(filePath);
                    }
                    catch (TagLib.UnsupportedFormatException ex)
                    {
                        _logger.LogWarning($"File: {filePath} : UnsupportedFormatException: {ex.Message}");
                        CopyFaultyFiles(filePath);
                    }
                }
            }
        }

        private static void CopyFaultyFiles(string filePath)
        {
            if (File.Exists(filePath.Replace(_folder, _outputRootFolderBadProcess)))
            {
                _logger.LogDebug($"File {filePath} already in Bad folder");
                return;
            }

            // Create directory in _bad folder structure
            Directory.CreateDirectory(
                Path.GetDirectoryName(filePath)
                .Replace(_folder, _outputRootFolderBadProcess));

            // Copy File to "Bad" folder
            File.Copy(filePath, filePath.Replace(_folder, _outputRootFolderBadProcess));
            File.Copy($"{filePath}.json", $"{filePath}.json".Replace(_folder, _outputRootFolderBadProcess));
            _logger.LogDebug($"Copied file {filePath} to Bad folder {filePath.Replace(_folder, _outputRootFolderBadProcess)}");
        }
    }
}
