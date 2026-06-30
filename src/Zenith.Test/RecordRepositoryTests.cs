using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Zenith.Data;

namespace Zenith.Test;

public class RecordRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly RecordRepository _repository;

    public RecordRepositoryTests()
    {
        _dbPath = Path.GetTempFileName();
        _repository = new RecordRepository(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Ignore if it cannot be deleted
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesSuccessfully()
    {
        // Act
        await _repository.InitializeAsync();

        // Assert
        // We can just verify it doesn't throw, and that a subsequent Insert doesn't throw.
        var records = await _repository.GetAllAsync();
        Assert.Empty(records);
    }

    [Fact]
    public async Task InsertAsync_AddsRecordAndReturnsId()
    {
        // Arrange
        await _repository.InitializeAsync();
        var record = new Zenith.Data.Record
        {
            FileName = "test.mp4",
            FilePath = "C:\\test.mp4",
            Duration = TimeSpan.FromMinutes(5),
            FileSize = 1024 * 1024,
            CreatedAt = DateTime.UtcNow,
            Resolution = "1920x1080",
            Codec = "H264",
            ThumbnailPath = "C:\\test.jpg"
        };

        // Act
        var id = await _repository.InsertAsync(record);

        // Assert
        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsInsertedRecordsInDescendingOrder()
    {
        // Arrange
        await _repository.InitializeAsync();
        var record1 = new Zenith.Data.Record
        {
            FileName = "test1.mp4",
            FilePath = "C:\\test1.mp4",
            Duration = TimeSpan.FromMinutes(1),
            FileSize = 100,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            Resolution = "1920x1080",
            Codec = "H264",
            ThumbnailPath = "C:\\test1.jpg"
        };
        
        var record2 = new Zenith.Data.Record
        {
            FileName = "test2.mp4",
            FilePath = "C:\\test2.mp4",
            Duration = TimeSpan.FromMinutes(2),
            FileSize = 200,
            CreatedAt = DateTime.UtcNow, // More recent
            Resolution = "1280x720",
            Codec = "H265",
            ThumbnailPath = "C:\\test2.jpg"
        };

        await _repository.InsertAsync(record1);
        await _repository.InsertAsync(record2);

        // Act
        var records = (await _repository.GetAllAsync()).ToList();

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("test2.mp4", records[0].FileName); // Record 2 should be first because of DESC CreatedAt
        Assert.Equal("test1.mp4", records[1].FileName);
        
        // Verify TimeSpan mapping
        Assert.Equal(TimeSpan.FromMinutes(2), records[0].Duration);
        
        Assert.Equal(record2.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), records[0].CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
}
