using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VisionOps.AI.Inference;
using VisionOps.Core.Models;

namespace VisionOps.AI.Models;

/// <summary>
/// YOLOv8n detector optimized for people detection with INT8 quantization
/// Target: <200ms inference latency, 6.3MB model size
/// </summary>
public class YoloV8Detector : IDisposable
{
    private readonly ILogger<YoloV8Detector> _logger;
    private readonly SharedInferenceEngine _inferenceEngine;
    private readonly ArrayPool<float> _floatPool;
    private readonly string _modelPath;
    private readonly string[] _labels;

    private const string ModelName = "yolov8n";
    private const int ImageSize = 640; // YOLOv8 input size
    private const float ConfidenceThreshold = 0.5f;
    private const float NmsThreshold = 0.4f;

    // COCO class IDs for people and vehicles
    private readonly HashSet<int> _targetClasses = new() { 0, 2, 5, 7 }; // person, car, bus, truck

    // Pre-allocated buffers for batch processing
    private readonly int _maxBatchSize;
    private float[]? _inputBuffer;
    private bool _disposed;

    public YoloV8Detector(
        ILogger<YoloV8Detector> logger,
        SharedInferenceEngine inferenceEngine,
        string modelPath = "models/yolov8n.onnx",
        int maxBatchSize = 16)
    {
        _logger = logger;
        _inferenceEngine = inferenceEngine;
        _modelPath = modelPath;
        _maxBatchSize = maxBatchSize;
        _floatPool = ArrayPool<float>.Shared;

        // COCO labels (80 classes)
        _labels = InitializeCocoLabels();

        // Pre-allocate input buffer for batch processing
        int bufferSize = maxBatchSize * 3 * ImageSize * ImageSize;
        _inputBuffer = _floatPool.Rent(bufferSize);
        Array.Clear(_inputBuffer, 0, bufferSize);
    }

    /// <summary>
    /// Initialize model and warm up with dummy inference
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing YOLOv8n detector with INT8 quantization");

        // Load model into shared session
        var session = await _inferenceEngine.GetSession(_modelPath, ModelName);

        // Warm up with dummy inference
        await WarmUpAsync(session);

        _logger.LogInformation("YOLOv8n detector initialized successfully");
    }

    /// <summary>
    /// Detect objects in a single frame
    /// </summary>
    public async Task<List<Detection>> DetectAsync(
        byte[] imageData,
        string cameraId,
        int frameNumber)
    {
        using var image = Image.Load<Rgb24>(imageData);
        var tensor = PreprocessImage(image, 0);

        var detections = await _inferenceEngine.RunInference(
            ModelName,
            async (session) => await RunSingleInference(session, tensor, cameraId, frameNumber));

        return detections;
    }

    /// <summary>
    /// Batch detection for multiple frames (8-16 frames optimal)
    /// </summary>
    public async Task<BatchDetectionResult> DetectBatchAsync(
        List<byte[]> images,
        string cameraId,
        int startFrameNumber)
    {
        if (images.Count == 0)
            return new BatchDetectionResult { BatchSize = 0 };

        int batchSize = Math.Min(images.Count, _maxBatchSize);
        _logger.LogDebug("Processing batch of {Size} frames for camera {Camera}",
            batchSize, cameraId);

        // Preprocess all images in parallel
        var preprocessTasks = images.Take(batchSize)
            .Select((img, idx) => Task.Run(() =>
            {
                using var image = Image.Load<Rgb24>(img);
                return PreprocessImage(image, idx);
            })).ToArray();

        var tensors = await Task.WhenAll(preprocessTasks);

        // Combine into batch tensor
        var batchTensor = CreateBatchTensor(tensors);

        // Run batch inference
        var startTime = DateTime.UtcNow;
        var detections = await _inferenceEngine.RunBatchInference(
            ModelName,
            async (session, size) => await RunBatchInference(
                session, batchTensor, size, cameraId, startFrameNumber),
            batchSize);

        var inferenceTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Flatten results
        var allDetections = detections.SelectMany(d => d).ToList();

        return new BatchDetectionResult
        {
            Detections = allDetections,
            BatchSize = batchSize,
            InferenceTimeMs = inferenceTime,
            ModelName = ModelName,
            IsQuantized = true
        };
    }

    /// <summary>
    /// Preprocess image for YOLOv8 input
    /// </summary>
    private DenseTensor<float> PreprocessImage(Image<Rgb24> image, int batchIndex)
    {
        // Resize to 640x640 with padding
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ImageSize, ImageSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black
        }));

        // Create tensor and normalize
        var tensor = _inferenceEngine.CreatePooledTensor(new[] { 1, 3, ImageSize, ImageSize });

        int pixelIndex = 0;
        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                var pixel = image[x, y];

                // Normalize to [0, 1] and layout as CHW
                tensor[0, 0, y, x] = pixel.R / 255.0f;
                tensor[0, 1, y, x] = pixel.G / 255.0f;
                tensor[0, 2, y, x] = pixel.B / 255.0f;
                pixelIndex++;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Create batch tensor from individual tensors
    /// </summary>
    private DenseTensor<float> CreateBatchTensor(DenseTensor<float>[] tensors)
    {
        int batchSize = tensors.Length;
        var batchTensor = _inferenceEngine.CreatePooledTensor(
            new[] { batchSize, 3, ImageSize, ImageSize });

        for (int b = 0; b < batchSize; b++)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int h = 0; h < ImageSize; h++)
                {
                    for (int w = 0; w < ImageSize; w++)
                    {
                        batchTensor[b, c, h, w] = tensors[b][0, c, h, w];
                    }
                }
            }
        }

        // Return individual tensors to pool
        foreach (var tensor in tensors)
        {
            _inferenceEngine.ReturnPooledTensor(tensor);
        }

        return batchTensor;
    }

    /// <summary>
    /// Run single frame inference
    /// </summary>
    private async Task<List<Detection>> RunSingleInference(
        InferenceSession session,
        DenseTensor<float> inputTensor,
        string cameraId,
        int frameNumber)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", inputTensor)
        };

        using var results = await Task.Run(() => session.Run(inputs));
        var output = results.First().AsEnumerable<float>().ToArray();

        _inferenceEngine.ReturnPooledTensor(inputTensor);

        return PostProcess(output, 1, cameraId, frameNumber);
    }

    /// <summary>
    /// Run batch inference
    /// </summary>
    private async Task<List<List<Detection>>> RunBatchInference(
        InferenceSession session,
        DenseTensor<float> batchTensor,
        int batchSize,
        string cameraId,
        int startFrameNumber)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", batchTensor)
        };

        using var results = await Task.Run(() => session.Run(inputs));
        var output = results.First().AsEnumerable<float>().ToArray();

        _inferenceEngine.ReturnPooledTensor(batchTensor);

        // Split output by batch
        var detectionsByFrame = new List<List<Detection>>();
        int outputSizePerBatch = output.Length / batchSize;

        for (int b = 0; b < batchSize; b++)
        {
            var batchOutput = new float[outputSizePerBatch];
            Array.Copy(output, b * outputSizePerBatch, batchOutput, 0, outputSizePerBatch);

            var detections = PostProcess(batchOutput, 1, cameraId, startFrameNumber + b);
            detectionsByFrame.Add(detections);
        }

        return detectionsByFrame;
    }

    /// <summary>
    /// Post-process YOLOv8 output with NMS
    /// </summary>
    private List<Detection> PostProcess(
        float[] output,
        int batchSize,
        string cameraId,
        int frameNumber)
    {
        var detections = new List<Detection>();

        // YOLOv8 output format: [1, 84, 8400]
        // 84 = 4 bbox coords + 80 class scores
        int numAnchors = 8400;
        int numClasses = 80;
        int stride = 84;

        var candidates = new List<Detection>();

        for (int i = 0; i < numAnchors; i++)
        {
            int baseIdx = i * stride;

            // Get bbox coordinates (cx, cy, w, h)
            float cx = output[baseIdx];
            float cy = output[baseIdx + 1];
            float w = output[baseIdx + 2];
            float h = output[baseIdx + 3];

            // Find best class
            float maxScore = 0;
            int classId = -1;

            for (int c = 0; c < numClasses; c++)
            {
                float score = output[baseIdx + 4 + c];
                if (score > maxScore && _targetClasses.Contains(c))
                {
                    maxScore = score;
                    classId = c;
                }
            }

            // Filter by confidence threshold
            if (maxScore > ConfidenceThreshold && classId >= 0)
            {
                candidates.Add(new Detection
                {
                    ClassId = classId,
                    Label = _labels[classId],
                    Confidence = maxScore,
                    X = (cx - w / 2) / ImageSize,
                    Y = (cy - h / 2) / ImageSize,
                    Width = w / ImageSize,
                    Height = h / ImageSize,
                    CameraId = cameraId,
                    FrameNumber = frameNumber,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        // Apply Non-Maximum Suppression
        return ApplyNMS(candidates);
    }

    /// <summary>
    /// Apply Non-Maximum Suppression to remove overlapping detections
    /// </summary>
    private List<Detection> ApplyNMS(List<Detection> detections)
    {
        if (detections.Count == 0)
            return detections;

        // Sort by confidence
        detections.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        var keep = new List<Detection>();
        var suppress = new HashSet<int>();

        for (int i = 0; i < detections.Count; i++)
        {
            if (suppress.Contains(i))
                continue;

            keep.Add(detections[i]);

            // Suppress overlapping detections
            for (int j = i + 1; j < detections.Count; j++)
            {
                if (suppress.Contains(j))
                    continue;

                float iou = detections[i].CalculateIoU(detections[j]);
                if (iou > NmsThreshold)
                {
                    suppress.Add(j);
                }
            }
        }

        return keep;
    }

    /// <summary>
    /// Warm up model with dummy inference
    /// </summary>
    private async Task WarmUpAsync(InferenceSession session)
    {
        _logger.LogDebug("Warming up YOLOv8 model");

        // Create dummy input
        var dummyTensor = _inferenceEngine.CreatePooledTensor(new[] { 1, 3, ImageSize, ImageSize });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", dummyTensor)
        };

        // Run dummy inference
        using var results = await Task.Run(() => session.Run(inputs));

        _inferenceEngine.ReturnPooledTensor(dummyTensor);

        _logger.LogDebug("YOLOv8 model warmed up successfully");
    }

    /// <summary>
    /// Initialize COCO labels
    /// </summary>
    private string[] InitializeCocoLabels()
    {
        return new[]
        {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck",
            "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
            "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra",
            "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
            "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
            "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup",
            "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
            "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
            "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
            "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
            "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier",
            "toothbrush"
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_inputBuffer != null)
        {
            _floatPool.Return(_inputBuffer, clearArray: true);
            _inputBuffer = null;
        }

        _disposed = true;
    }
}