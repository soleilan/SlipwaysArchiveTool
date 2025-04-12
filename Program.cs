using Slipways.Boids.Teams;
using Slipways.General.SWAR;
using System.IO;
using System.Text;
using static Slipways.General.SWAR.SwarFile;

class Program
{
    static long ReadChecksum(string filePath)
    {
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var binaryReader = new BinaryReader(fileStream))
        {
            // Skip the "SWAR" header
            binaryReader.BaseStream.Seek(4, SeekOrigin.Begin);
            // Read the checksum
            byte[] checksumBytes = binaryReader.ReadBytes(8);
            Array.Reverse(checksumBytes);
            long checksum = BitConverter.ToInt64(checksumBytes, 0);
            return checksum;
        }
    }

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Drag a .swar or folder onto the .exe to begin unpacking or repacking.");
            Console.ReadKey();
            return;
        }
        try
        {
            string pathGiven = args[0];

            if (Directory.Exists(pathGiven))
            {
                Repack(pathGiven);
            }
            else if (File.Exists(pathGiven))
            {
                Unpack(new SwarFile(pathGiven));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No file or directory found at whatever " + pathGiven + " is.");
                Console.ResetColor();
            }
        }
        catch (UnauthorizedAccessException e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Access to the path is denied. Please check your permissions." + Environment.NewLine);
            Console.WriteLine(e);
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to load swarfile!" + Environment.NewLine);
            Console.WriteLine(e);
            Console.ResetColor();
        }
        Console.ReadKey();
    }

    static void Unpack(SwarFile swar)
    {
        Dictionary<string, FileEntry> fileIndex = swar.DecodeFileIndex().Item1;

        foreach (var entry in fileIndex)
        {
            string filePath = entry.Key;
            FileEntry fileData = entry.Value;
            Stream stream = swar.OpenForReading(filePath);

            if (stream == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(filePath + " could not be read!");
                Console.ResetColor();
                continue;
            }

            using (stream)
            {
                string outputPath = Path.Combine("output", filePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                using (FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                    Console.WriteLine($"Unpacked: {outputPath}");
                }
            }
        }
        Console.WriteLine("Unpacking has finished!" + Environment.NewLine);
    }


    static void Repack(string folderPath)
    {
        var filesFromFolder = new Dictionary<string, byte[]>();

        // Read files from the directory
        foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(folderPath, file).Replace("\\", "/");
            byte[] fileData = File.ReadAllBytes(file);
            filesFromFolder[relativePath] = fileData;
        }

        using (var memoryStream = new MemoryStream())
        using (var binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8))
        {
            // Write header and placeholder for integrity
            binaryWriter.Write(Encoding.ASCII.GetBytes("SWAR"));
            binaryWriter.Write(new byte[8]); // Placeholder for integrity

            // Calculate the size of the file index
            long indexSize = 0;
            foreach (var file in filesFromFolder)
            {
                indexSize += Encoding.UTF8.GetByteCount(file.Key) + 1; // filename + null terminator
                indexSize += 8; // offset (4 bytes) + length (4 bytes)
            }
            indexSize += 1; // End of file index null terminator

            // Write a placeholder for the file index
            long indexStart = memoryStream.Position;
            binaryWriter.Write(new byte[indexSize]);

            // Write the actual file data and track offsets
            var fileEntries = new List<(string name, int offset, int length)>();
            foreach (var file in filesFromFolder)
            {
                int offset = (int)memoryStream.Position;
                int length = file.Value.Length;

                fileEntries.Add((file.Key, offset, length));
                binaryWriter.Write(file.Value);
            }

            // Calculate checksum before updating the file index
            byte[] swarContents = memoryStream.ToArray();
            long checksum = 0;
            for (int i = (int)indexStart; i < swarContents.Length; i++)
            {
                checksum = (((checksum ^ swarContents[i]) << 1) & 0xFFFFFFFFFFFFL) | (checksum >> 47);
            }

            // Go back to the file index and properly write it
            using (var indexStream = new MemoryStream(swarContents))
            using (var indexWriter = new BinaryWriter(indexStream, Encoding.UTF8))
            {
                indexWriter.Seek((int)indexStart, SeekOrigin.Begin); // Move to the start of the file index

                foreach (var entry in fileEntries)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(entry.name);
                    indexWriter.Write(nameBytes);
                    indexWriter.Write((byte)0); // Null terminator for the string

                    // Write the offset and length as big-endian
                    indexWriter.Write(BitConverter.GetBytes(entry.offset).Reverse().ToArray());
                    indexWriter.Write(BitConverter.GetBytes(entry.length).Reverse().ToArray());
                }

                indexWriter.Write((byte)0); // End of file index null terminator
            }

            // Update the checksum in the SWAR file
            byte[] checksumBytes = BitConverter.GetBytes(checksum).Reverse().ToArray();
            Array.Copy(checksumBytes, 0, swarContents, 4, 8);

            // Save to file
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.swar");
            File.WriteAllBytes(outputPath, swarContents);

            // Verify the written checksum
            long writtenChecksum = ReadChecksum(outputPath);

            Console.WriteLine("Repacking has finished!");
        }
    }


}
