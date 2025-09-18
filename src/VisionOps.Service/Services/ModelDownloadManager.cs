using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisionOps.Service.Services;

/// <summary>
/// Downloads and manages AI models from official open-source repositories.
/// Supports multiple sources: GitHub releases, HuggingFace, ONNX Model Zoo
/// </summary>
public class ModelDownloadManager : BackgroundService
{
    private readonly ILogger<ModelDownloadManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _modelsPath;
    private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

    // Model sources configuration
    private readonly List<ModelSource> _modelSources = new()
    {
        // YOLOv8 models from Ultralytics (official)
        new ModelSource
        {
            Name = "YOLOv8n-Detection",
            FileName = "yolov8n.onnx",
            Sources = new[]
            {
                new DownloadSource
                {
                    Url = "https://github.com/ultralytics/assets/releases/download/v8.0.0/yolov8n.onnx",
                    Size = 6_244_608,
                    Type = "GitHub Release"
                },
                // Backup source from HuggingFace
                new DownloadSource
                {
                    Url = "https://huggingface.co/Ultralytics/YOLOv8/resolve/main/yolov8n.onnx",
                    Size = 6_244_608,
                    Type = "HuggingFace"
                }
            },
            Required = true,
            Description = "YOLOv8 nano model for object detection (people, vehicles)"
        },

        // Additional YOLOv8 variant for better accuracy (optional)
        new ModelSource
        {
            Name = "YOLOv8s-Detection",
            FileName = "yolov8s.onnx",
            Sources = new[]
            {
                new DownloadSource
                {
                    Url = "https://github.com/ultralytics/assets/releases/download/v8.0.0/yolov8s.onnx",
                    Size = 22_432_512, // ~21.4MB
                    Type = "GitHub Release"
                }
            },
            Required = false,
            Description = "YOLOv8 small model for improved accuracy (optional)"
        },

        // CLIP model for vision-language tasks (alternative to Florence-2)
        new ModelSource
        {
            Name = "CLIP-ViT-B32",
            FileName = "clip-vit-base-patch32.onnx",
            Sources = new[]
            {
                new DownloadSource
                {
                    Url = "https://huggingface.co/sentence-transformers/clip-ViT-B-32/resolve/main/onnx/model.onnx",
                    Size = 340_000_000, // ~324MB
                    Type = "HuggingFace"
                }
            },
            Required = false,
            Description = "CLIP model for image-text understanding"
        },

        // Lightweight alternative: MobileNet for scene classification
        new ModelSource
        {
            Name = "MobileNetV3-Scene",
            FileName = "mobilenetv3-large.onnx",
            Sources = new[]
            {
                new DownloadSource
                {
                    Url = "https://github.com/onnx/models/raw/main/validated/vision/classification/mobilenet/model/mobilenetv3-large-1.0-224.onnx",
                    Size = 21_893_808, // ~20.9MB
                    Type = "ONNX Model Zoo"
                }
            },
            Required = false,
            Description = "Lightweight model for scene classification"
        },

        // TinyYOLO for ultra-low resource scenarios
        new ModelSource
        {
            Name = "TinyYOLOv4",
            FileName = "yolov4-tiny.onnx",
            Sources = new[]
            {
                new DownloadSource
                {
                    Url = "https://github.com/onnx/models/raw/main/validated/vision/object_detection_segmentation/tiny-yolov4/model/tiny-yolov4.onnx",
                    Size = 23_017_216, // ~22MB
                    Type = "ONNX Model Zoo"
                }
            },
            Required = false,
            Description = "Tiny YOLO for resource-constrained environments"
        }
    };

    public ModelDownloadManager(ILogger<ModelDownloadManager> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30), // Large models need more time
            DefaultRequestHeaders =
            {
                { "User-Agent", "VisionOps/1.0 ModelDownloader" }
            }
        };

        _modelsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "VisionOps", "models");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Let service start

            _logger.LogInformation("Model Download Manager started");

            // Ensure models directory exists
            Directory.CreateDirectory(_modelsPath);

            // Check and download required models
            await DownloadRequiredModels(stoppingToken);

            // Create model catalog
            await CreateModelCatalog();

            _logger.LogInformation("Model download check complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download manager failed");
            // Don't crash the service - it can work without AI initially
        }
    }

    private async Task DownloadRequiredModels(CancellationToken cancellationToken)
    {
        var requiredModels = _modelSources.Where(m => m.Required).ToList();
        var downloadTasks = new List<Task>();

        foreach (var model in requiredModels)
        {
            var modelPath = Path.Combine(_modelsPath, model.FileName);

            if (File.Exists(modelPath))
            {
                _logger.LogInformation("Model {Name} already exists at {Path}", model.Name, modelPath);
                continue;
            }

            downloadTasks.Add(DownloadModelAsync(model, cancellationToken));
        }

        if (downloadTasks.Any())
        {
            _logger.LogInformation("Downloading {Count} required models...", downloadTasks.Count);
            await Task.WhenAll(downloadTasks);
        }

        // Check for optional models (don't block on these)
        _ = Task.Run(async () => await DownloadOptionalModels(cancellationToken), cancellationToken);
    }

    private async Task DownloadOptionalModels(CancellationToken cancellationToken)
    {
        var optionalModels = _modelSources.Where(m => !m.Required).ToList();

        foreach (var model in optionalModels)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var modelPath = Path.Combine(_modelsPath, model.FileName);
            if (File.Exists(modelPath)) continue;

            try
            {
                _logger.LogInformation("Downloading optional model: {Name}", model.Name);
                await DownloadModelAsync(model, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download optional model {Name}", model.Name);
            }
        }
    }

    private async Task DownloadModelAsync(ModelSource model, CancellationToken cancellationToken)
    {
        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            var targetPath = Path.Combine(_modelsPath, model.FileName);
            var tempPath = targetPath + ".downloading";

            // Try each source until one succeeds
            foreach (var source in model.Sources)
            {
                try
                {
                    _logger.LogInformation("Downloading {Name} from {Type}: {Url}",
                        model.Name, source.Type, source.Url);

                    // Special handling for HuggingFace URLs
                    if (source.Type == "HuggingFace")
                    {
                        // Add authorization header if needed
                        // For public models, this isn't required
                    }

                    using var response = await _httpClient.GetAsync(source.Url,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Failed to download from {Url}: {Status}",
                            source.Url, response.StatusCode);
                        continue;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? source.Size;

                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await CopyWithProgress(contentStream, fileStream, totalBytes, model.Name, cancellationToken);
                    }

                    // Verify size
                    var fileInfo = new FileInfo(tempPath);
                    if (fileInfo.Length < source.Size * 0.9) // Allow 10% variance
                    {
                        _logger.LogWarning("Downloaded file size mismatch for {Name}", model.Name);
                        File.Delete(tempPath);
                        continue;
                    }

                    // Move to final location
                    File.Move(tempPath, targetPath, true);
                    _logger.LogInformation("Successfully downloaded {Name} ({Size:F1} MB)",
                        model.Name, fileInfo.Length / 1_000_000.0);

                    break; // Success, don't try other sources
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download {Name} from {Source}",
                        model.Name, source.Type);

                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    // Try next source
                }
            }

            // Check if download succeeded
            if (!File.Exists(targetPath))
            {
                throw new InvalidOperationException($"Failed to download {model.Name} from any source");
            }
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task CopyWithProgress(Stream source, Stream destination, long totalBytes,
        string modelName, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920]; // 80KB buffer
        var totalRead = 0L;
        var lastReportTime = DateTime.UtcNow;
        var lastReportBytes = 0L;

        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read == 0) break;

            await destination.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;

            // Report progress every second
            var now = DateTime.UtcNow;
            if ((now - lastReportTime).TotalSeconds >= 1)
            {
                var progress = (int)(totalRead * 100 / totalBytes);
                var speedMBps = (totalRead - lastReportBytes) / (1024.0 * 1024.0) / (now - lastReportTime).TotalSeconds;

                _logger.LogInformation("Downloading {Model}: {Progress}% ({Speed:F1} MB/s)",
                    modelName, progress, speedMBps);

                lastReportTime = now;
                lastReportBytes = totalRead;
            }
        }
    }

    private async Task CreateModelCatalog()
    {
        var catalog = new
        {
            Generated = DateTime.UtcNow,
            ModelsPath = _modelsPath,
            Models = new List<object>()
        };

        foreach (var modelSource in _modelSources)
        {
            var modelPath = Path.Combine(_modelsPath, modelSource.FileName);
            if (File.Exists(modelPath))
            {
                var fileInfo = new FileInfo(modelPath);
                catalog.Models.Add(new
                {
                    modelSource.Name,
                    modelSource.FileName,
                    modelSource.Description,
                    modelSource.Required,
                    SizeMB = fileInfo.Length / 1_000_000.0,
                    Downloaded = fileInfo.CreationTime,
                    Available = true
                });
            }
            else
            {
                catalog.Models.Add(new
                {
                    modelSource.Name,
                    modelSource.FileName,
                    modelSource.Description,
                    modelSource.Required,
                    Available = false
                });
            }
        }

        var catalogPath = Path.Combine(_modelsPath, "model-catalog.json");
        var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(catalogPath, json);

        _logger.LogInformation("Model catalog created at {Path}", catalogPath);
    }

    public override void Dispose()
    {
        _downloadSemaphore?.Dispose();
        _httpClient?.Dispose();
        base.Dispose();
    }

    // Model configuration classes
    private class ModelSource
    {
        public required string Name { get; init; }
        public required string FileName { get; init; }
        public required DownloadSource[] Sources { get; init; }
        public required bool Required { get; init; }
        public required string Description { get; init; }
    }

    private class DownloadSource
    {
        public required string Url { get; init; }
        public required long Size { get; init; }
        public required string Type { get; init; }
    }
}