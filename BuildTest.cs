// Quick test to verify all the fixes compile
using Microsoft.Extensions.Logging;
using VisionOps.Video.Memory;
using VisionOps.Video.Discovery;
using VisionOps.Video.Compression;
using VisionOps.Data;
using VisionOps.AI.Optimization;
using Microsoft.EntityFrameworkCore;

namespace VisionOps.BuildTest;

public class BuildTest
{
    public static void TestAllFixedClasses()
    {
        // Test 1: VisionOpsDbContext - SQLite configuration fixed
        var optionsBuilder = new DbContextOptionsBuilder<VisionOpsDbContext>();
        optionsBuilder.UseSqlite("Data Source=test.db");
        using var dbContext = new VisionOpsDbContext(optionsBuilder.Options);

        // Test 2: ModelQuantizer - DenseTensor using statement added
        var logger2 = LoggerFactory.Create(builder => { }).CreateLogger<ModelQuantizer>();
        var quantizer = new ModelQuantizer(logger2);

        // Test 3: WebPCompressor - Using SixLabors.ImageSharp instead of WebPWrapper
        var logger3 = LoggerFactory.Create(builder => { }).CreateLogger<WebPCompressor>();
        var compressor = new WebPCompressor(logger3);

        // Test 4: OnvifDiscoveryService - Using OnvifDiscovery package correctly
        var logger4 = LoggerFactory.Create(builder => { }).CreateLogger<OnvifDiscoveryService>();
        var discovery = new OnvifDiscoveryService(logger4);

        // Test 5: TimestampedFrame - No duplicate definition
        var frame = new TimestampedFrame(
            new byte[100],
            320,
            240,
            OpenCvSharp.MatType.CV_8UC3,
            DateTime.UtcNow,
            null
        );

        // Test 6: FrameBufferPool - Nullable references fixed
        var logger6 = LoggerFactory.Create(builder => { }).CreateLogger<FrameBufferPool>();
        var pool = new FrameBufferPool(logger6);

        // Test 7: CircularFrameBuffer - Uses correct TimestampedFrame
        var logger7 = LoggerFactory.Create(builder => { }).CreateLogger<CircularFrameBuffer>();
        var buffer = new CircularFrameBuffer("camera1", pool, logger7);

        Console.WriteLine("All build fixes verified successfully!");
    }
}