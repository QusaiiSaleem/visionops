using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VisionOps.AI.Inference;
using VisionOps.Core.Models;

namespace VisionOps.AI.Models;

/// <summary>
/// Florence-2 vision-language model processor for generating scene descriptions
/// Target: <1s per frame, 120MB INT8 quantized model
/// </summary>
public class Florence2Processor : IDisposable
{
    private readonly ILogger<Florence2Processor> _logger;
    private readonly SharedInferenceEngine _inferenceEngine;
    private readonly Florence2Config _config;
    private readonly ArrayPool<float> _floatPool;
    private readonly SemaphoreSlim _processingSemaphore;

    private const string ModelName = "florence2-base";
    private const int ImageSize = 384; // Florence-2 requirement
    private const int MaxDescriptionLength = 64; // Tokens
    private const int EmbeddingDimension = 768; // Florence-2 embedding size

    // Tokenizer for text generation
    private Tokenizer? _tokenizer;
    private readonly Dictionary<int, string> _vocabulary = new();

    // Track last key frame time per camera
    private readonly ConcurrentDictionary<string, DateTime> _lastKeyFrameTimes = new();

    // Pre-allocated buffers
    private float[]? _imageBuffer;
    private bool _disposed;

    public Florence2Processor(
        ILogger<Florence2Processor> logger,
        SharedInferenceEngine inferenceEngine,
        Florence2Config? config = null)
    {
        _logger = logger;
        _inferenceEngine = inferenceEngine;
        _config = config ?? new Florence2Config();
        _floatPool = ArrayPool<float>.Shared;
        _processingSemaphore = new SemaphoreSlim(1, 1); // Sequential processing

        // Pre-allocate image buffer
        _imageBuffer = _floatPool.Rent(3 * ImageSize * ImageSize);
        Array.Clear(_imageBuffer, 0, 3 * ImageSize * ImageSize);
    }

    /// <summary>
    /// Initialize Florence-2 model and tokenizer
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Florence-2 vision-language model");

        // Load model into shared session
        var session = await _inferenceEngine.GetSession(_config.ModelPath, ModelName);

        // Initialize tokenizer
        await InitializeTokenizerAsync();

        // Warm up with dummy inference
        await WarmUpAsync(session);

        _logger.LogInformation("Florence-2 processor initialized (120MB INT8 quantized)");
    }

    /// <summary>
    /// Check if a key frame should be generated for this camera
    /// </summary>
    public bool ShouldGenerateKeyFrame(string cameraId)
    {
        if (!_lastKeyFrameTimes.TryGetValue(cameraId, out var lastTime))
        {
            return true; // First frame for this camera
        }

        var elapsed = DateTime.UtcNow - lastTime;
        return elapsed.TotalSeconds >= _config.KeyFrameIntervalSeconds;
    }

    /// <summary>
    /// Generate a key frame with description
    /// </summary>
    public async Task<KeyFrame?> GenerateKeyFrameAsync(
        byte[] imageData,
        string cameraId,
        int frameNumber,
        List<Detection>? detections = null)
    {
        // Check if we should generate a key frame
        if (!ShouldGenerateKeyFrame(cameraId))
        {
            return null;
        }

        await _processingSemaphore.WaitAsync();
        try
        {
            var startTime = DateTime.UtcNow;

            // Load and preprocess image
            using var image = Image.Load<Rgb24>(imageData);

            // Resize for Florence-2 (384x384)
            var floImage = ResizeForFlorence(image);

            // Generate description
            var (description, embeddings) = await GenerateDescriptionAsync(floImage);

            // Create thumbnail for storage (320x240)
            var thumbnail = CreateThumbnail(image);
            var compressedImage = await CompressWebPAsync(thumbnail);

            // Update last key frame time
            _lastKeyFrameTimes[cameraId] = DateTime.UtcNow;

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return new KeyFrame
            {
                CameraId = cameraId,
                FrameNumber = frameNumber,
                Timestamp = DateTime.UtcNow,
                CompressedImage = compressedImage,
                Description = description,
                Embeddings = embeddings,
                PeopleCount = detections?.Count(d => d.Label == "person") ?? 0,
                DetectedObjects = detections?.Select(d => d.Label).Distinct().ToList() ?? new(),
                ProcessingTimeMs = processingTime,
                LocationId = Environment.MachineName // Use machine name as location ID
            };
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    /// <summary>
    /// Generate description and embeddings from image
    /// </summary>
    private async Task<(string description, float[] embeddings)> GenerateDescriptionAsync(
        Image<Rgb24> image)
    {
        // Prepare input tensor
        var inputTensor = PreprocessForFlorence(image);

        var result = await _inferenceEngine.RunInference(
            ModelName,
            async (session) =>
            {
                // Create task prompt tensor
                var taskPrompt = CreateTaskPromptTensor("What is happening in this image?");

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("pixel_values", inputTensor),
                    NamedOnnxValue.CreateFromTensor("input_ids", taskPrompt)
                };

                using var outputs = await Task.Run(() => session.Run(inputs));

                // Extract text tokens and embeddings
                var textTokens = outputs.First(o => o.Name == "output_ids")
                    .AsEnumerable<long>().ToArray();
                var embeddings = outputs.First(o => o.Name == "encoder_hidden_states")
                    .AsEnumerable<float>().ToArray();

                // Decode tokens to text
                var description = DecodeTokens(textTokens);

                // Average pool embeddings for search vector
                var pooledEmbeddings = AveragePoolEmbeddings(embeddings);

                return (description, pooledEmbeddings);
            });

        _inferenceEngine.ReturnPooledTensor(inputTensor);

        return result;
    }

    /// <summary>
    /// Preprocess image for Florence-2 input
    /// </summary>
    private DenseTensor<float> PreprocessForFlorence(Image<Rgb24> image)
    {
        // Resize to 384x384
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ImageSize, ImageSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black
        }));

        // Create tensor with normalization
        var tensor = _inferenceEngine.CreatePooledTensor(new[] { 1, 3, ImageSize, ImageSize });

        // Florence-2 normalization values
        float[] mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        float[] std = { 0.26862954f, 0.26130258f, 0.27577711f };

        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                var pixel = image[x, y];

                // Normalize with Florence-2 specific values
                tensor[0, 0, y, x] = ((pixel.R / 255.0f) - mean[0]) / std[0];
                tensor[0, 1, y, x] = ((pixel.G / 255.0f) - mean[1]) / std[1];
                tensor[0, 2, y, x] = ((pixel.B / 255.0f) - mean[2]) / std[2];
            }
        }

        return tensor;
    }

    /// <summary>
    /// Create task prompt tensor for Florence-2
    /// </summary>
    private DenseTensor<long> CreateTaskPromptTensor(string prompt)
    {
        // Simple tokenization (in production, use proper tokenizer)
        var tokens = new List<long> { 101 }; // [CLS] token

        // Add prompt tokens (simplified)
        foreach (char c in prompt.ToLower())
        {
            tokens.Add(c); // Simplified tokenization
        }

        tokens.Add(102); // [SEP] token

        // Pad to max length
        while (tokens.Count < MaxDescriptionLength)
        {
            tokens.Add(0); // [PAD] token
        }

        var tensor = new DenseTensor<long>(new[] { 1, tokens.Count });
        for (int i = 0; i < tokens.Count; i++)
        {
            tensor[0, i] = tokens[i];
        }

        return tensor;
    }

    /// <summary>
    /// Decode output tokens to text description
    /// </summary>
    private string DecodeTokens(long[] tokens)
    {
        if (_tokenizer != null)
        {
            // Use tokenizer if available
            var text = _tokenizer.Decode(tokens.Select(t => (int)t).ToArray());
            return CleanDescription(text);
        }

        // Fallback to simple decoding
        var words = new List<string>();
        foreach (var token in tokens)
        {
            if (token == 0 || token == 101 || token == 102) // Skip special tokens
                continue;

            if (_vocabulary.TryGetValue((int)token, out var word))
            {
                words.Add(word);
            }
        }

        var description = string.Join(" ", words);
        return CleanDescription(description);
    }

    /// <summary>
    /// Clean and format description
    /// </summary>
    private string CleanDescription(string text)
    {
        // Remove special tokens and clean up
        text = text.Replace("[CLS]", "")
                   .Replace("[SEP]", "")
                   .Replace("[PAD]", "")
                   .Trim();

        // Limit length
        if (text.Length > 200)
        {
            text = text.Substring(0, 197) + "...";
        }

        // Ensure proper sentence structure
        if (!string.IsNullOrEmpty(text) && !text.EndsWith('.'))
        {
            text += ".";
        }

        return text;
    }

    /// <summary>
    /// Average pool embeddings for semantic search
    /// </summary>
    private float[] AveragePoolEmbeddings(float[] embeddings)
    {
        var pooled = new float[EmbeddingDimension];
        int numTokens = embeddings.Length / EmbeddingDimension;

        for (int i = 0; i < embeddings.Length; i++)
        {
            pooled[i % EmbeddingDimension] += embeddings[i];
        }

        // Average
        for (int i = 0; i < EmbeddingDimension; i++)
        {
            pooled[i] /= numTokens;
        }

        // L2 normalize for cosine similarity
        float norm = 0;
        for (int i = 0; i < EmbeddingDimension; i++)
        {
            norm += pooled[i] * pooled[i];
        }
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (int i = 0; i < EmbeddingDimension; i++)
            {
                pooled[i] /= norm;
            }
        }

        return pooled;
    }

    /// <summary>
    /// Resize image for Florence-2 (384x384)
    /// </summary>
    private Image<Rgb24> ResizeForFlorence(Image<Rgb24> original)
    {
        var resized = original.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ImageSize, ImageSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black
        }));

        return resized;
    }

    /// <summary>
    /// Create thumbnail for storage (320x240)
    /// </summary>
    private Image<Rgb24> CreateThumbnail(Image<Rgb24> original)
    {
        var thumbnail = original.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(_config.ThumbnailSize.Width, _config.ThumbnailSize.Height),
            Mode = ResizeMode.Max
        }));

        return thumbnail;
    }

    /// <summary>
    /// Compress image to WebP format
    /// </summary>
    private async Task<byte[]> CompressWebPAsync(Image<Rgb24> image)
    {
        using var ms = new MemoryStream();

        // Save as WebP with low quality for small size (3-5KB target)
        await image.SaveAsWebpAsync(ms, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
        {
            Quality = _config.CompressionQuality,
            Method = SixLabors.ImageSharp.Formats.Webp.WebpEncodingMethod.BestQuality
        });

        var compressed = ms.ToArray();

        _logger.LogDebug("Compressed image to {Size} bytes (Q={Quality})",
            compressed.Length, _config.CompressionQuality);

        return compressed;
    }

    /// <summary>
    /// Initialize tokenizer from vocabulary file
    /// </summary>
    private async Task InitializeTokenizerAsync()
    {
        try
        {
            if (File.Exists(_config.TokenizerPath))
            {
                var vocabJson = await File.ReadAllTextAsync(_config.TokenizerPath);
                var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(vocabJson);

                if (vocab != null)
                {
                    // Create reverse vocabulary for decoding
                    foreach (var kvp in vocab)
                    {
                        _vocabulary[kvp.Value] = kvp.Key;
                    }
                }

                _logger.LogInformation("Loaded Florence-2 vocabulary with {Count} tokens",
                    _vocabulary.Count);
            }
            else
            {
                _logger.LogWarning("Tokenizer vocabulary not found at {Path}, using fallback",
                    _config.TokenizerPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize tokenizer");
        }
    }

    /// <summary>
    /// Warm up model with dummy inference
    /// </summary>
    private async Task WarmUpAsync(InferenceSession session)
    {
        _logger.LogDebug("Warming up Florence-2 model");

        // Create dummy inputs
        var dummyImage = _inferenceEngine.CreatePooledTensor(new[] { 1, 3, ImageSize, ImageSize });
        var dummyPrompt = new DenseTensor<long>(new[] { 1, MaxDescriptionLength });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", dummyImage),
            NamedOnnxValue.CreateFromTensor("input_ids", dummyPrompt)
        };

        try
        {
            using var results = await Task.Run(() => session.Run(inputs));
            _logger.LogDebug("Florence-2 model warmed up successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warm-up inference failed, continuing anyway");
        }
        finally
        {
            _inferenceEngine.ReturnPooledTensor(dummyImage);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_imageBuffer != null)
        {
            _floatPool.Return(_imageBuffer, clearArray: true);
            _imageBuffer = null;
        }

        _processingSemaphore?.Dispose();

        // Tokenizer doesn't implement IDisposable
        _tokenizer = null;

        _disposed = true;
    }
}