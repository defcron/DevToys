using DevToys.Api;
using Microsoft.Extensions.Logging;
using OneOf;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DevToys.Loaf.Helpers;

internal static partial class LoafHelper
{
    internal const string LoafHeaderPattern = @"^SHA256\(-\)=([0-9a-f]+) (.*)$";
    
    [GeneratedRegex(LoafHeaderPattern, RegexOptions.Compiled)]
    private static partial Regex LoafHeaderRegex();

    /// <summary>
    /// Creates a .loaf archive from input data
    /// </summary>
    internal static async Task<ResultInfo<string>> CreateLoafAsync(
        OneOf<FileInfo, string> input,
        IFileStorage fileStorage,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get input stream
            ResultInfo<Stream> inputResult = await input.GetStreamAsync(fileStorage, cancellationToken);
            if (!inputResult.HasSucceeded)
            {
                return new ResultInfo<string>(string.Empty, false);
            }

            using Stream inputStream = inputResult.Data!;
            
            // Create tar archive in memory
            using var tarStream = new MemoryStream();
            await CreateTarArchiveAsync(inputStream, tarStream, GetArchiveName(input), cancellationToken);
            
            // Compress with gzip
            using var gzipStream = new MemoryStream();
            tarStream.Position = 0;
            using (var gzip = new GZipStream(gzipStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                await tarStream.CopyToAsync(gzip, cancellationToken);
            }
            
            // Convert to hex
            gzipStream.Position = 0;
            byte[] gzipData = gzipStream.ToArray();
            string hexData = Convert.ToHexString(gzipData).ToLowerInvariant();
            
            // Calculate SHA256 hash
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hexData));
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            
            // Create .loaf format: SHA256(-)=<hash> <hex-data>
            string loafContent = $"SHA256(-)={hash} {hexData}";
            
            return new ResultInfo<string>(loafContent, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create .loaf archive");
            return new ResultInfo<string>(string.Empty, false);
        }
    }

    /// <summary>
    /// Verifies a .loaf archive integrity
    /// </summary>
    internal static Task<ResultInfo<bool>> VerifyLoafAsync(
        string loafContent,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                var match = LoafHeaderRegex().Match(loafContent);
                if (!match.Success)
                {
                    logger.LogWarning("Invalid .loaf format: missing or malformed header");
                    return new ResultInfo<bool>(false, false);
                }

                string embeddedHash = match.Groups[1].Value;
                string hexData = match.Groups[2].Value;

                // Validate hash length (should be exactly 64 hex characters for SHA256)
                if (embeddedHash.Length != 64)
                {
                    logger.LogWarning("Invalid .loaf format: hash length is not 64 characters");
                    return new ResultInfo<bool>(false, true); // Operation succeeds, but validation fails
                }

                // Calculate actual hash
                using var sha256 = SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hexData));
                string calculatedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                bool isValid = string.Equals(embeddedHash, calculatedHash, StringComparison.OrdinalIgnoreCase);

                return new ResultInfo<bool>(isValid, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to verify .loaf archive");
                return new ResultInfo<bool>(false, false);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts contents from a .loaf archive
    /// </summary>
    internal static async Task<ResultInfo<List<ExtractedFile>>> ExtractLoafAsync(
        string loafContent,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var match = LoafHeaderRegex().Match(loafContent);
            if (!match.Success)
            {
                logger.LogWarning("Invalid .loaf format: missing or malformed header");
                return new ResultInfo<List<ExtractedFile>>(new List<ExtractedFile>(), false);
            }
            
            string hexData = match.Groups[2].Value;
            
            // Convert hex to bytes
            if (hexData.Length % 2 != 0)
            {
                logger.LogWarning("Invalid hex data: odd length");
                return new ResultInfo<List<ExtractedFile>>(new List<ExtractedFile>(), false);
            }
            
            byte[] gzipData = new byte[hexData.Length / 2];
            for (int i = 0; i < hexData.Length; i += 2)
            {
                gzipData[i / 2] = byte.Parse(hexData.AsSpan(i, 2), NumberStyles.HexNumber);
            }
            
            // Decompress gzip
            using var gzipStream = new MemoryStream(gzipData);
            using var decompressStream = new GZipStream(gzipStream, CompressionMode.Decompress);
            using var tarStream = new MemoryStream();
            await decompressStream.CopyToAsync(tarStream, cancellationToken);
            
            // Extract tar contents
            tarStream.Position = 0;
            var extractedFiles = await ExtractTarArchiveAsync(tarStream, cancellationToken);
            
            return new ResultInfo<List<ExtractedFile>>(extractedFiles, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract .loaf archive");
            return new ResultInfo<List<ExtractedFile>>(new List<ExtractedFile>(), false);
        }
    }

    private static string GetArchiveName(OneOf<FileInfo, string> input)
    {
        return input.Match(
            fileInfo => fileInfo.Name,
            str => "-"  // Use "-" for string input to match official LoaF convention
        );
    }

    private static async Task CreateTarArchiveAsync(Stream inputStream, Stream outputStream, string fileName, CancellationToken cancellationToken)
    {
        // Simple tar implementation for single file
        // Read all input data
        using var inputData = new MemoryStream();
        await inputStream.CopyToAsync(inputData, cancellationToken);
        byte[] fileData = inputData.ToArray();
        
        // Create tar header (simplified)
        byte[] header = new byte[512];
        
        // File name (100 bytes)
        byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
        Array.Copy(nameBytes, 0, header, 0, Math.Min(nameBytes.Length, 100));
        
        // File mode (8 bytes) - "0000644\0"
        byte[] modeBytes = Encoding.ASCII.GetBytes("0000644");
        Array.Copy(modeBytes, 0, header, 100, modeBytes.Length);
        
        // Owner UID (8 bytes) - "0000000\0"
        byte[] uidBytes = Encoding.ASCII.GetBytes("0000000");
        Array.Copy(uidBytes, 0, header, 108, uidBytes.Length);
        
        // Group GID (8 bytes) - "0000000\0"
        byte[] gidBytes = Encoding.ASCII.GetBytes("0000000");
        Array.Copy(gidBytes, 0, header, 116, gidBytes.Length);
        
        // File size (12 bytes) - octal representation
        string sizeOctal = Convert.ToString(fileData.Length, 8).PadLeft(11, '0');
        byte[] sizeBytes = Encoding.ASCII.GetBytes(sizeOctal);
        Array.Copy(sizeBytes, 0, header, 124, sizeBytes.Length);
        
        // Modification time (12 bytes) - current time in octal
        long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string timeOctal = Convert.ToString(unixTime, 8).PadLeft(11, '0');
        byte[] timeBytes = Encoding.ASCII.GetBytes(timeOctal);
        Array.Copy(timeBytes, 0, header, 136, timeBytes.Length);
        
        // File type (1 byte) - '0' for regular file
        header[156] = (byte)'0';
        
        // Calculate checksum
        int checksum = 0;
        for (int i = 0; i < 512; i++)
        {
            if (i >= 148 && i < 156) // checksum field itself is treated as spaces
                checksum += 32;
            else
                checksum += header[i];
        }
        
        // Checksum (8 bytes) - octal with trailing null and space
        string checksumOctal = Convert.ToString(checksum, 8).PadLeft(6, '0');
        byte[] checksumBytes = Encoding.ASCII.GetBytes(checksumOctal);
        Array.Copy(checksumBytes, 0, header, 148, checksumBytes.Length);
        header[154] = 0; // null terminator
        header[155] = 32; // space
        
        // Write header
        await outputStream.WriteAsync(header, cancellationToken);
        
        // Write file data
        await outputStream.WriteAsync(fileData, cancellationToken);
        
        // Pad to 512-byte boundary
        int padding = (512 - (fileData.Length % 512)) % 512;
        if (padding > 0)
        {
            byte[] paddingBytes = new byte[padding];
            await outputStream.WriteAsync(paddingBytes, cancellationToken);
        }
        
        // Write end-of-archive (two empty 512-byte blocks)
        byte[] endMarker = new byte[1024];
        await outputStream.WriteAsync(endMarker, cancellationToken);
    }

    private static async Task<List<ExtractedFile>> ExtractTarArchiveAsync(Stream tarStream, CancellationToken cancellationToken)
    {
        var files = new List<ExtractedFile>();
        byte[] headerBuffer = new byte[512];
        
        while (true)
        {
            // Read tar header
            int headerBytesRead = await tarStream.ReadAsync(headerBuffer, cancellationToken);
            if (headerBytesRead < 512)
                break;
                
            // Check if this is the end marker (all zeros)
            bool isEndMarker = true;
            for (int i = 0; i < 512; i++)
            {
                if (headerBuffer[i] != 0)
                {
                    isEndMarker = false;
                    break;
                }
            }
            if (isEndMarker)
                break;
            
            // Extract file name
            int nameLength = 0;
            for (int i = 0; i < 100; i++)
            {
                if (headerBuffer[i] == 0)
                    break;
                nameLength++;
            }
            string fileName = Encoding.UTF8.GetString(headerBuffer, 0, nameLength);
            
            // Extract file size
            string sizeStr = Encoding.ASCII.GetString(headerBuffer, 124, 11).TrimEnd('\0', ' ');
            
            long fileSize;
            // TAR format uses octal for file sizes
            try
            {
                fileSize = Convert.ToInt64(sizeStr, 8);
            }
            catch
            {
                // Fallback to decimal if octal parsing fails
                if (!long.TryParse(sizeStr, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, 
                    CultureInfo.InvariantCulture, out fileSize))
                {
                    fileSize = 0;
                }
            }
            
            // Read file data
            byte[] fileData = new byte[fileSize];
            if (fileSize > 0)
            {
                int totalBytesRead = 0;
                while (totalBytesRead < fileSize)
                {
                    int bytesRead = await tarStream.ReadAsync(fileData.AsMemory(totalBytesRead, (int)(fileSize - totalBytesRead)), cancellationToken);
                    if (bytesRead == 0)
                        break;
                    totalBytesRead += bytesRead;
                }
                
                // Skip padding to next 512-byte boundary
                long padding = (512 - (fileSize % 512)) % 512;
                if (padding > 0)
                {
                    byte[] paddingBuffer = new byte[padding];
                    await tarStream.ReadAsync(paddingBuffer, cancellationToken);
                }
            }
            
            files.Add(new ExtractedFile(fileName, fileData));
        }
        
        return files;
    }
}

/// <summary>
/// Represents a file extracted from a .loaf archive
/// </summary>
internal record ExtractedFile(string Name, byte[] Data);