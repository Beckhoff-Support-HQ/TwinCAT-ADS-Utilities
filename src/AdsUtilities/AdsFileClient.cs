using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TwinCAT.Ads;

namespace AdsUtilities;

public class AdsFileClient : AdsClientBase
{
    public AdsFileClient(ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        
    }

    private async Task<uint> FileOpenAsync(
        string path,
        uint openFlags,
        CancellationToken cancel = default)
    {
        byte[] wrBfr = Encoding.ASCII.GetBytes(path + '\0');
        byte[] rdBfr = new byte[sizeof(UInt32)];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rwResult = await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFOpen,
            openFlags,
            rdBfr,
            wrBfr,
            cancel);

        if (rwResult.ErrorCode is AdsErrorCode.DeviceNotFound)
            _logger?.LogError("Could not open file '{filePath}' on {netId} because the file was not found.", path, NetId);

        rwResult.ThrowOnError();

        return BitConverter.ToUInt32(rdBfr);
    }

    internal async Task<uint> FileOpenReadingAsync(
        string relativePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        bool binaryOpen = true,
        CancellationToken cancel = default)
    {
        uint tmpOpenMode = (uint)AdsIndexOffsets.SysServFOpenReading | ((uint)basePath);
        if (binaryOpen) tmpOpenMode |= (uint)AdsIndexOffsets.SysServFOpenAsBinary;

        return await FileOpenAsync(relativePath, tmpOpenMode, cancel);
    }

    private async Task<uint> FileOpenWritingAsync(
        string relativePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        bool binaryOpen = true,
        CancellationToken cancel = default)
    {
        uint tmpOpenMode = (uint)AdsIndexOffsets.SysServFOpenWriting | ((uint)basePath);
        if (binaryOpen) tmpOpenMode |= (uint)AdsIndexOffsets.SysServFOpenAsBinary;

        return await FileOpenAsync(relativePath, tmpOpenMode, cancel);
    }

    private async Task FileCloseAsync(uint hFile, CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFClose,
            hFile,
            Array.Empty<byte>(),
            Array.Empty<byte>(),
            cancel);
    }

    private async Task<FileInfoByteMapped> GetFileInfoBytesAsync(
        string filePath,
        CancellationToken cancel = default)
    {
        uint hFile = await FileOpenReadingAsync(filePath, AdsDirectory.Generic, false, cancel);

        byte[] wrBfr = Encoding.UTF8.GetBytes(filePath);
        byte[] rdBfr = new byte[Marshal.SizeOf(typeof(FileInfoByteMapped))];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFFind,
            hFile,
            rdBfr,
            wrBfr,
            cancel);

        await FileCloseAsync(hFile, cancel);

        return StructConverter.MarshalToStructure<FileInfoByteMapped>(rdBfr);
    }

    public async Task<byte[]> FileReadFullAsync(
        string filePath,
        bool binaryOpen = true,
        CancellationToken cancel = default)
    {
        long fileSize = (await GetFileInfoAsync(filePath, cancel)).fileSize;
        byte[] rdBfr = new byte[fileSize];

        uint hFile = await FileOpenReadingAsync(
            filePath,
            AdsDirectory.Generic,
            binaryOpen,
            cancel);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFRead,
            hFile,
            rdBfr,
            new byte[4],
            cancel);

        await FileCloseAsync(hFile, cancel);

        return rdBfr.ToArray();
    }

    public async Task FileReadFullAsync(
        byte[] readBuffer,
        string relativePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        bool binaryOpen = true,
        CancellationToken cancel = default)
    {
        uint hFile = await FileOpenReadingAsync(
            relativePath,
            basePath,
            binaryOpen,
            cancel);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFRead,
            hFile,
            readBuffer,
            new byte[4],
            cancel);

        await FileCloseAsync(hFile, cancel);
    }

    public async Task FileCopyAsync(
        string pathSource,
        AdsFileClient destinationFileClient,
        string pathDestination,
        bool binaryOpen = true,
        IProgress<double>? progress = null,
        uint chunkSizeKB = 100,
        CancellationToken cancel = default)
    {
        var fileInfo = await GetFileInfoAsync(pathSource, cancel);
        long fileSize = fileInfo.fileSize;
        uint chunkSizeBytes = chunkSizeKB * 1000;
        long bytesCopied = 0;

        uint hFileRead = await FileOpenReadingAsync(
            pathSource,
            AdsDirectory.Generic,
            binaryOpen,
            cancel);

        uint hFileWrite = await destinationFileClient.FileOpenWritingAsync(
            pathDestination,
            AdsDirectory.Generic,
            binaryOpen,
            cancel);

        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            byte[] fileContentBuffer = await FileReadChunkAsync(
                hFileRead,
                chunkSizeBytes,
                cancel);

            await destinationFileClient.FileWriteChunkAsync(
                hFileWrite,
                fileContentBuffer,
                cancel);

            if (progress is not null)
            {
                bytesCopied += fileContentBuffer.Length;
                double progressPercentage = 100 * (double)bytesCopied / fileSize;
                progress.Report(progressPercentage);
            }

            if (fileContentBuffer.Length == chunkSizeBytes)
                continue;
            break;
        }
        await FileCloseAsync(hFileRead, cancel);
        await destinationFileClient.FileCloseAsync(hFileWrite, cancel);
    }

    public async Task FileCopyAsync(
        string pathSource,
        string pathDestination,
        bool binaryOpen = true,
        IProgress<double>? progress = null,
        uint chunkSizeBytes = 10_000,
        CancellationToken cancel = default)
        => await FileCopyAsync(
            pathSource,
            this,
            pathDestination,
            binaryOpen,
            progress,
            chunkSizeBytes,
            cancel);

    private async Task FileWriteChunkAsync(
        uint hFile,
        byte[] chunk,
        CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFWrite,
            hFile,
            new byte[4],
            chunk,
            cancel);
    }

    private async Task<byte[]> FileReadChunkAsync(
        uint hFile,
        uint chunkSize,
        CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[chunkSize];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var readWriteResult = await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFRead,
            hFile,
            rdBfr,
            new byte[4],
            cancel);

        if (readWriteResult.ReadBytes < chunkSize)
            return rdBfr.Take(readWriteResult.ReadBytes).ToArray();

        return rdBfr.ToArray();
    }

    public async Task FileWriteFullAsync(
        byte[] data,
        string relativeFilePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        bool binaryOpen = true,
        CancellationToken cancel = default)
    {
        uint hFile = await FileOpenWritingAsync(
            relativeFilePath,
            basePath,
            binaryOpen,
            cancel);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFWrite,
            hFile,
            new byte[4],
            data,
            cancel);

        await FileCloseAsync(hFile, cancel);
    }

    public async Task DeleteFileAsync(
        string relativeFilePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFDelete,
            (uint)basePath,
            Array.Empty<byte>(),
            Encoding.UTF8.GetBytes(relativeFilePath),
            cancel);
    }

    public async Task RenameFileAsync(
        string filePathCurrent,
        string filePathNew,
        CancellationToken cancel = default)
    {
        WriteRequestHelper renameRequest = new WriteRequestHelper()
            .AddStringUTF8(filePathCurrent)
            .AddStringUTF8(filePathNew);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServFRead,
            1 << 16,
            Array.Empty<byte>(),
            renameRequest.GetBytes(),
            cancel);
    }

    public async Task<FileInfoDetails> GetFileInfoAsync(
        string filePath,
        CancellationToken cancel = default)
    {
        FileInfoByteMapped fileEntry = await GetFileInfoBytesAsync(filePath, cancel);
        return (FileInfoDetails)fileEntry;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServMkDir,
            (uint)AdsIndexOffsets.SysServFOpenPathGeneric,
            Array.Empty<byte>(),
            Encoding.UTF8.GetBytes(path),
            cancel);
    }

    public async Task<bool> FileExists(
        string relativeFilePath,
        AdsDirectory basePath = AdsDirectory.Generic,
        CancellationToken cancel = default)
    {
        try
        {
            uint hFile = await FileOpenReadingAsync(
                relativeFilePath,
                basePath,
                true,
                cancel);

            return hFile > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "FileExists check failed for {filePath}", relativeFilePath);
            return false;
        }
    }

    public async IAsyncEnumerable<FileInfoDetails> GetFolderContentStreamAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        if (path.EndsWith("/") || path.EndsWith("\\"))
            path += "*";
        else if (!path.EndsWith("*"))
            path += "/*";

        uint idxOffs = (uint)AdsIndexOffsets.SysServFOpenPathGeneric;
        byte[] nextFileBuffer = new WriteRequestHelper().AddStringUTF8(path).GetBytes();
        byte[] fileInfoBuffer = new byte[Marshal.SizeOf(typeof(FileInfoByteMapped))];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            Array.Clear(fileInfoBuffer, 0, fileInfoBuffer.Length);

            var rwResult = await adsConnection.ReadWriteAsync(
                (uint)AdsIndexGroups.SysServFFind,
                idxOffs,
                fileInfoBuffer,
                nextFileBuffer,
                cancel
            );

            if (rwResult.ErrorCode == AdsErrorCode.DeviceNotFound)
            {
                yield break;
            }
            else if (rwResult.ErrorCode != AdsErrorCode.NoError)
            {
                _logger?.LogError("Unexpected exception '{eMessage}' while getting folder content of '{path}' from '{netId}'. Results may be incomplete.",
                    rwResult.ErrorCode.ToString(), path, _netId);

                yield break;
            }

            FileInfoByteMapped latestFile = StructConverter.MarshalToStructure<FileInfoByteMapped>(fileInfoBuffer);
            FileInfoDetails latestFileDetails = (FileInfoDetails)latestFile;

            idxOffs = latestFile.hFile;
            nextFileBuffer = Array.Empty<byte>();

            if (latestFileDetails.fileName is "." or "..")
                continue;

            yield return latestFileDetails;
        }
    }

    public async Task<List<FileInfoDetails>> GetFolderContentListAsync(string path, CancellationToken cancel = default)
    {
        List<FileInfoDetails> folderContent = new();

        await foreach (var item in GetFolderContentStreamAsync(path, cancel))
        {
            folderContent.Add(item);
        }

        return folderContent;
    }

    public async Task StartProcessAsync(
        string applicationPath,
        string workingDirectory,
        string commandLineParameters,
        CancellationToken cancel = default)
    {
        WriteRequestHelper startProcessRequest = new WriteRequestHelper()
            .AddInt(applicationPath.Length)
            .AddInt(workingDirectory.Length)
            .AddInt(commandLineParameters.Length)
            .AddStringAscii(applicationPath)
            .AddStringAscii(workingDirectory)
            .AddStringAscii(commandLineParameters);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var res = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServStartProcess,
            0,
            startProcessRequest.GetBytes(),
            cancel);

        res.ThrowOnError();
    }

    public void StartProcess(
        string applicationPath,
        string workingDirectory,
        string commandLineParameters)
    {
        StartProcessAsync(
            applicationPath,
            workingDirectory,
            commandLineParameters)
            .GetAwaiter()
            .GetResult();
    }
}
