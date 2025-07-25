﻿using AdsUtilities;
using AdsUtilitiesUI.Views.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdsUtilitiesUI;

public class FileExplorerViewModel : INotifyPropertyChanged
{
    //private ILogger _logger;

    //private ILoggerFactory _LoggerFactory;

    public FileExplorerViewModel() //ILoggerFactory loggerFactory
    {
        //_LoggerFactory = loggerFactory;
        //_logger = _LoggerFactory.CreateLogger<FileExplorerViewModel>();
    }



    public event PropertyChangedEventHandler? PropertyChanged;

    private StaticRoutesInfo _Target;
    public StaticRoutesInfo Target
    {
        get => _Target;
        set
        {
            if(_Target != value)
            {
                _Target = value;
                LoadRootDirectories();              
            }
            
        }
    }

    private ObservableCollection<FileSystemItem> _rootItems = new ();
    public ObservableCollection<FileSystemItem> RootItems
    {
        get => _rootItems;
        set
        {
            _rootItems = value;
        }
    }

    private void LoadRootDirectories()
    {
        string rootDirectory = "/";   

        FileSystemItem rootFolder = new(
            DeviceNetID: Target.NetId,
            Name: rootDirectory,
            AlternativeName: string.Empty,
            IsDirectory: true,
            ParentItem: null,
            ParentDirectory: string.Empty,
            FileSize: 0,
            CreationTime: DateTime.Now,
            LastModifyTime: DateTime.Now,
            LastAccessTime: DateTime.Now,
            IsSystemFile: true,
            IsReadOnly : true,
            IsEncrypted : false,
            IsCompressed: false,
            IsRoot: true);

        rootFolder.LoadChildren();

        RootItems = new ObservableCollection<FileSystemItem> { rootFolder };
        OnPropertyChanged(nameof(RootItems));
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async Task CopyFile(FileSystemItem sourceFile, FileSystemItem targetFolder)
    {
        AdsFileClient sourceFileClient = new();
        if (!await sourceFileClient.Connect(sourceFile.DeviceNetID))
            return; 

        AdsFileClient destinationFileClient = new();
        if (!await destinationFileClient.Connect(targetFolder.DeviceNetID))
            return; 

        var progressWindow = new CopyProgressWindow(sourceFile, targetFolder);
        var cts = new CancellationTokenSource(); 
        progressWindow.CancellationRequested += cts.Cancel; // Event for Cancel-Button

        progressWindow.Show();

        var progress = new Progress<double>(progressWindow.SetProgress);

        try
        {
            await sourceFileClient.FileCopyAsync(   
                    $"{sourceFile.ParentDirectory.TrimEnd('/')}/{sourceFile.Name}",
                    destinationFileClient,
                    $"{targetFolder.ParentDirectory.TrimEnd('/')}/{targetFolder.Name}/{sourceFile.Name}",
                    true, progress, 100, cts.Token);

            await targetFolder.LoadChildren();              // Reload folder content to display file that was copied

            await Task.Delay(500, cts.Token);
        }
        catch (OperationCanceledException)
        {
            //_logger.LogInformation("Copying process aborted.");
        }
        catch (Exception ex)
        {
            //_logger.LogError(ex, "An error occurred while copying the file.");
        }
        finally
        {
            progressWindow.Close();

        }
    }
}


public class FileSystemItem
{
    public string DeviceNetID { get; set; }
    public string Name { get; set; }
    public string AlternativeName { get; set; }
    public bool IsDirectory { get; set; }
    public FileSystemItem? ParentItem { get; set; }
    public string? ParentDirectory { get; set; }
    public bool IsHidden { get; set; }
    public long FileSize { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastModifyTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public bool IsSystemFile { get; set; }
    public bool IsCompressed { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsEncrypted { get; set; }

    public BitmapImage Image { get; set; }
    public bool IsRoot { get; set; }

    public ObservableCollection<FileSystemItem> Children { get; set; } = new ObservableCollection<FileSystemItem>();

    public FileSystemItem(string DeviceNetID, string Name, bool IsDirectory, FileSystemItem ParentItem, string ParentDirectory, long FileSize, DateTime CreationTime, DateTime LastAccessTime, bool IsSystemFile, bool IsCompressed, bool IsRoot, bool IsReadOnly, bool IsEncrypted, string AlternativeName, DateTime LastModifyTime)
    {
        this.DeviceNetID = DeviceNetID;
        this.Name = Name;
        this.AlternativeName = AlternativeName;
        this.ParentItem = ParentItem;
        this.ParentDirectory = ParentDirectory;
        this.FileSize = FileSize;
        this.CreationTime = CreationTime;
        this.LastModifyTime = LastModifyTime;
        this.LastAccessTime = LastAccessTime;
        this.IsSystemFile = IsSystemFile;
        this.IsCompressed = IsCompressed;
        this.IsRoot = IsRoot;
        this.IsDirectory = IsDirectory;
        if (IsRoot)
        {
            Image = LoadBitmapImage("Images/harddrive.png");
        }
        else if (IsDirectory)
        {
            if (IsHidden)
                Image = LoadBitmapImage("Images/folderdrive_hidden.png");
            else
                Image = LoadBitmapImage("Images/folderdrive.png");
        }
        else
        {
            if (IsHidden)
                Image = LoadBitmapImage("Images/file_simple_hidden.png");
            else
                Image = LoadBitmapImage("Images/file_simple.png");
        }
    }

    public bool IsExecutable => !IsDirectory && Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true;

    public static string ConvertByteSize(long byteSize)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = byteSize;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F2} {units[unitIndex]}";
    }

    public async Task Rename(string newName)
    {
        AdsFileClient fileClient = new();
        await fileClient.Connect(DeviceNetID);
        await fileClient.RenameFileAsync($"{ParentDirectory}/{Name}", $"{ParentDirectory}/{newName}");
        Name = newName;
        await ParentItem?.LoadChildren();
    }

    public async Task Delete()
    {
        AdsFileClient fileClient = new();
        await fileClient.Connect(DeviceNetID);
        await fileClient.DeleteFileAsync($"{ParentDirectory}/{Name}");
        await ParentItem?.LoadChildren();
    }

    public async Task Run()
    {
        AdsFileClient adsFileClient = new();
        await adsFileClient.Connect(DeviceNetID);
        await adsFileClient.StartProcessAsync(ParentDirectory + "/" + Name, ParentDirectory, string.Empty);

        //await adsFileClient.StartProcessAsync(@"C:\Program Files\Notepad++\notepad++.exe", @"C:\Program Files\Notepad++", string.Empty);
    }

    public async Task LoadChildren()
    {
        AdsFileClient fileClient = new();
        await fileClient.Connect(DeviceNetID);
        string fullPath = System.IO.Path.Combine(ParentDirectory, Name);
        Children.Clear();

        await foreach (var file in fileClient.GetFolderContentStreamAsync(fullPath))
        {
            FileSystemItem item = new
            (
                DeviceNetID: DeviceNetID,
                Name: file.fileName,
                AlternativeName: file.alternativeFileName,
                IsDirectory: file.isDirectory,
                ParentItem : this,
                ParentDirectory: fullPath,
                FileSize: file.fileSize,
                CreationTime: file.creationTime,
                LastModifyTime: file.lastWriteTime,
                LastAccessTime: file.lastAccessTime,
                IsSystemFile: file.isSystemFile,
                IsCompressed: file.isCompressed,
                IsReadOnly : file.isReadOnly,
                IsEncrypted: file.isEncrypted,
                IsRoot: false
            );
            if (item.IsDirectory)
            {
                item.Children.Add(null); // Placeholder for Lazy Loading
            }

            Children.Add(item);
        }
    }
    private static BitmapImage LoadBitmapImage(string relativePath)
    {
        var uri = new Uri($"pack://application:,,,/{relativePath}");
        return new BitmapImage(uri);
    }
}
