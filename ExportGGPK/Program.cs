using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BrotliSharpLib;
using ImageMagick;
using LibDat;
using LibGGPK;
using LibGGPK.Records;

namespace ExportGGPK
{
    class Program
    {
        private static readonly GrindingGearsPackageContainer Container = new GrindingGearsPackageContainer();
        private static readonly Dictionary<string, List<FileRecord>> Data = new Dictionary<string, List<FileRecord>>();
        private static readonly char[] PathSeparator = { Path.DirectorySeparatorChar };

        private static string packagePath = string.Empty;
        private static string outputPath = string.Empty;
	    private static string packageTreePath = string.Empty;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ExportGGPK.exe <path to ggpk> <base output dir> [ggpk dir to extract]");
                return;
            }

            packagePath = args[0];
            outputPath = args[1];
            if (args.Length == 3)
	    {
                packageTreePath = args[2];
            }

            if (!File.Exists(packagePath))
            {
                Console.WriteLine($"Content pack file is not available at ${packagePath}");
                return;
            }

            Container.Read(packagePath, Console.WriteLine);

            var art = GetDirectory(@"Art\2DItems");
            var gameData = GetDirectory("Data");

            var artItems = RecursiveFindByType(FileRecord.DataFormat.TextureDds, art);
            var dataItems = RecursiveFindByType(FileRecord.DataFormat.Dat, gameData);

            Console.WriteLine($"Found {artItems.Count} textures!");
            Console.WriteLine($"Found {dataItems.Count} data files!");

            Extract(artItems);
            ConvertTextures(outputPath);
            Extract(dataItems);
            ConvertData(outputPath);
        }

        private static void ConvertData(string path)
        {
            var enumerator = Directory.EnumerateFiles(path, "*.dat", SearchOption.AllDirectories);
            Parallel.ForEach(enumerator, (record) =>
            {
                try
                {
                    var dat = new DatContainer(record);
                    var csvData = dat.GetCsvFormat();
                    var csvName = Path.ChangeExtension(record, ".csv");
                    File.WriteAllText(csvName, csvData);
                    Console.WriteLine($"Converted data file {csvName}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error trying to convert data file {record}!");
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }
            });
        }

        private static void ConvertTextures(string path)
        {
            var enumerator = Directory.EnumerateFiles(path, "*.dds", SearchOption.AllDirectories);
            Parallel.ForEach(enumerator, (record) =>
            {
                var raw = File.ReadAllBytes(record);
                // if the data is like "*Art/2DItems/Armours/BodyArmours/DexInt3A.dds"
                // then follow that path and update our raw binary data
                if (raw[0] == 0x2A)
                {
                    var refPath = Encoding.ASCII.GetString(raw.Skip(1).Take(raw.Length - 1).ToArray());
                    var newPath = $"{path}/ROOT/{refPath}";
                    raw = File.ReadAllBytes(newPath);
                }

                // pull the first four bytes as the expected file length
                var rawLength = raw.Take(4).ToArray();
                // the integer is little endian
                rawLength.Reverse();
                var expectedLength = BitConverter.ToUInt32(rawLength, 0);
                if (raw.Length - 4 != expectedLength)
                {
                    Console.WriteLine($"Warning, mismatch between expected length {expectedLength} and actual length {raw.Length}");
                }
                // now decompress the rest of the file
                var decompressed = Brotli.DecompressBuffer(raw, 4, raw.Length - 4);
                // check for valid header
                if (decompressed[0] == decompressed[1] && decompressed[1] == 0x44 && decompressed[2] == 0x53 && decompressed[3] == 0x20)
                {
                    var newFile = Path.ChangeExtension(record, ".png");
                    if (File.Exists(newFile))
                    {
                        return;
                    }

                    using (var magickImage = new MagickImage(decompressed))
                    {
                        magickImage.Write(newFile);
                    }
                }
                else
                {
                    Console.WriteLine($"Error decompressing texture {record}");
                }
            });
        }

        private static void Extract(IEnumerable<FileRecord> items)
        {
            foreach (var item in items)
            {
                item.ExtractFileWithDirectoryStructure(packagePath, outputPath);
            }
        }

        private static DirectoryTreeNode GetDirectory(string path)
        {
            var dirs = path.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var currDir = Container.DirectoryRoot;

            foreach (var dir in dirs)
            {
                currDir = WalkNode(currDir, dir);
            }

            return currDir;
        }

        private static DirectoryTreeNode WalkNode(DirectoryTreeNode start, string subDirectory)
        {
            return start.Children.Find((val) => val.Name == subDirectory);
        }

        public static IEnumerable<FileRecord> GetFiles(string subDirectory)
        {
            var dir = WalkNode(Container.DirectoryRoot, subDirectory);
            return dir == null ? new List<FileRecord>() : dir.Files;
        }

        public static IEnumerable<FileRecord> FilterByType(FileRecord.DataFormat format, List<FileRecord> records)
        {
            return records.Where((record) => record.FileFormat.Equals(format));
        }

        private static List<FileRecord> RecursiveFindByType(FileRecord.DataFormat type, DirectoryTreeNode currentNode, List<FileRecord> roller = null)
        {
            if (roller == null)
            {
                roller = new List<FileRecord>();
            }

            roller.AddRange(FilterByType(type, currentNode.Files));

            foreach (var subDir in currentNode.Children)
            {
                RecursiveFindByType(type, subDir, roller);
            }

            return roller;
        }
    }
}
