using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace ZZZ.Editor.Profiling
{
    [InitializeOnLoad]
    internal static class ProfilerCaptureAnalyzer
    {
        private const string CAPTURE_DIRECTORY_NAME = "ProfilerCaptures";
        private const string SUMMARY_FILE_NAME = "ProfilerCaptureSummary.md";

        private static readonly Queue<string> PendingCapturePaths = new Queue<string>();
        private static readonly List<CaptureStatistics> CaptureResults = new List<CaptureStatistics>();

        private static bool _isAnalyzing;
        private static bool _isWaitingForLoad;
        private static string _currentCapturePath;

        static ProfilerCaptureAnalyzer()
        {
            EditorApplication.delayCall += AnalyzeIfNeeded;
        }

        [MenuItem("ZZZ/Profiling/Analyze Saved Captures")]
        private static void AnalyzeSavedCaptures()
        {
            StartAnalysis(force: true);
        }

        private static void AnalyzeIfNeeded()
        {
            StartAnalysis(force: false);
        }

        private static void StartAnalysis(bool force)
        {
            if (_isAnalyzing)
            {
                return;
            }

            string captureDirectory = GetCaptureDirectory();
            if (!Directory.Exists(captureDirectory))
            {
                return;
            }

            string[] capturePaths = Directory.GetFiles(captureDirectory, "*.data")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (capturePaths.Length == 0)
            {
                return;
            }

            string summaryPath = Path.Combine(captureDirectory, SUMMARY_FILE_NAME);
            if (!force && File.Exists(summaryPath))
            {
                DateTime summaryTime = File.GetLastWriteTimeUtc(summaryPath);
                string summary = File.ReadAllText(summaryPath);
                bool containsEveryCapture = capturePaths.All(path =>
                    summary.Contains($"| {Path.GetFileNameWithoutExtension(path)} |"));
                if (containsEveryCapture && capturePaths.All(path => File.GetLastWriteTimeUtc(path) <= summaryTime))
                {
                    return;
                }
            }

            PendingCapturePaths.Clear();
            CaptureResults.Clear();
            foreach (string capturePath in capturePaths)
            {
                PendingCapturePaths.Enqueue(capturePath);
            }

            _isAnalyzing = true;
            ProfilerDriver.profileLoaded += OnProfileLoaded;
            LoadNextCapture();
        }

        private static void LoadNextCapture()
        {
            if (PendingCapturePaths.Count == 0)
            {
                FinishAnalysis();
                return;
            }

            _currentCapturePath = PendingCapturePaths.Dequeue();
            _isWaitingForLoad = true;
            if (ProfilerDriver.LoadProfile(_currentCapturePath, keepExistingData: false))
            {
                return;
            }

            _isWaitingForLoad = false;
            Debug.LogError($"Failed to load Profiler capture: {_currentCapturePath}");
            LoadNextCapture();
        }

        private static void OnProfileLoaded()
        {
            if (!_isAnalyzing || !_isWaitingForLoad)
            {
                return;
            }

            _isWaitingForLoad = false;
            EditorApplication.delayCall += AnalyzeLoadedCapture;
        }

        private static void AnalyzeLoadedCapture()
        {
            try
            {
                CaptureResults.Add(ReadCapture(_currentCapturePath));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            LoadNextCapture();
        }

        private static CaptureStatistics ReadCapture(string capturePath)
        {
            var result = new CaptureStatistics(Path.GetFileNameWithoutExtension(capturePath));
            int frameIndex = ProfilerDriver.firstFrameIndex;
            int lastFrameIndex = ProfilerDriver.lastFrameIndex;

            while (frameIndex >= 0 && frameIndex <= lastFrameIndex)
            {
                using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, 0))
                {
                    if (frameData.valid)
                    {
                        result.AddFrame(
                            frameIndex,
                            frameData.frameTimeMs,
                            frameData.frameGpuTimeMs,
                            GetGcAllocBytes(frameIndex),
                            ReadCounter(frameData, "Total Used Memory"),
                            ReadCounter(frameData, "Batches Count"),
                            ReadCounter(frameData, "SetPass Calls Count"),
                            ReadCounter(frameData, "Draw Calls Count"),
                            ReadCounter(frameData, "Triangles Count"));
                    }
                }

                int nextFrameIndex = ProfilerDriver.GetNextFrameIndex(frameIndex);
                if (nextFrameIndex < 0 || nextFrameIndex == frameIndex)
                {
                    break;
                }

                frameIndex = nextFrameIndex;
            }

            result.Calculate();
            return result;
        }

        private static long GetGcAllocBytes(int frameIndex)
        {
            long totalGcAllocBytes = 0;
            int gcAllocMarkerId = FrameDataView.invalidMarkerId;

            for (int threadIndex = 0; ; threadIndex++)
            {
                using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (!frameData.valid)
                    {
                        break;
                    }

                    if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
                    {
                        gcAllocMarkerId = frameData.GetMarkerId("GC.Alloc");
                        if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
                        {
                            break;
                        }
                    }

                    for (int sampleIndex = 0; sampleIndex < frameData.sampleCount; sampleIndex++)
                    {
                        if (frameData.GetSampleMarkerId(sampleIndex) == gcAllocMarkerId &&
                            frameData.GetSampleMetadataCount(sampleIndex) > 0)
                        {
                            totalGcAllocBytes += frameData.GetSampleMetadataAsLong(sampleIndex, 0);
                        }
                    }
                }
            }

            return totalGcAllocBytes;
        }

        private static long? ReadCounter(RawFrameDataView frameData, string counterName)
        {
            int markerId = frameData.GetMarkerId(counterName);
            if (markerId == FrameDataView.invalidMarkerId || !frameData.HasCounterValue(markerId))
            {
                return null;
            }

            return frameData.GetCounterValueAsLong(markerId);
        }

        private static void FinishAnalysis()
        {
            ProfilerDriver.profileLoaded -= OnProfileLoaded;
            _isAnalyzing = false;
            _isWaitingForLoad = false;
            _currentCapturePath = null;

            string summaryPath = Path.Combine(GetCaptureDirectory(), SUMMARY_FILE_NAME);
            File.WriteAllText(summaryPath, BuildSummary(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Debug.Log($"Profiler capture summary saved: {summaryPath}");
        }

        private static string BuildSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Profiler Capture Summary");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();
            builder.AppendLine("| Capture | Frames | Duration (s) | CPU avg (ms) | CPU p95 (ms) | CPU p99 (ms) | CPU max (ms) | Avg FPS | GC avg (B/frame) | GC max (B) | Used memory avg (MB) | Used memory max (MB) | Batches avg | SetPass avg | Draw calls avg | Triangles avg |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CaptureStatistics result in CaptureResults)
            {
                builder.Append("| ").Append(result.Name)
                    .Append(" | ").Append(result.FrameCount)
                    .Append(" | ").Append(Format(result.DurationSeconds))
                    .Append(" | ").Append(Format(result.AverageFrameTimeMs))
                    .Append(" | ").Append(Format(result.P95FrameTimeMs))
                    .Append(" | ").Append(Format(result.P99FrameTimeMs))
                    .Append(" | ").Append(Format(result.MaxFrameTimeMs))
                    .Append(" | ").Append(Format(result.AverageFps))
                    .Append(" | ").Append(Format(result.AverageGcAllocBytes))
                    .Append(" | ").Append(result.MaxGcAllocBytes)
                    .Append(" | ").Append(FormatMegabytes(result.AverageUsedMemoryBytes))
                    .Append(" | ").Append(FormatMegabytes(result.MaxUsedMemoryBytes))
                    .Append(" | ").Append(FormatNullable(result.AverageBatches))
                    .Append(" | ").Append(FormatNullable(result.AverageSetPassCalls))
                    .Append(" | ").Append(FormatNullable(result.AverageDrawCalls))
                    .Append(" | ").Append(FormatNullable(result.AverageTriangles))
                    .AppendLine(" |");
            }

            builder.AppendLine();
            builder.AppendLine("CPU frame time comes from the main thread frame data. GPU time can be unavailable on Android Vulkan captures. GC allocation is summed across all captured threads.");
            return builder.ToString();
        }

        private static string GetCaptureDirectory()
        {
            string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectDirectory, CAPTURE_DIRECTORY_NAME);
        }

        private static string Format(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatNullable(double? value)
        {
            return value.HasValue ? Format(value.Value) : "N/A";
        }

        private static string FormatMegabytes(double? bytes)
        {
            return bytes.HasValue ? Format(bytes.Value / (1024d * 1024d)) : "N/A";
        }

        private sealed class CaptureStatistics
        {
            private readonly List<double> _frameTimesMs = new List<double>();
            private readonly NumericAccumulator _usedMemoryBytes = new NumericAccumulator();
            private readonly NumericAccumulator _batches = new NumericAccumulator();
            private readonly NumericAccumulator _setPassCalls = new NumericAccumulator();
            private readonly NumericAccumulator _drawCalls = new NumericAccumulator();
            private readonly NumericAccumulator _triangles = new NumericAccumulator();
            private long _totalGcAllocBytes;

            public CaptureStatistics(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public int FrameCount => _frameTimesMs.Count;
            public double DurationSeconds { get; private set; }
            public double AverageFrameTimeMs { get; private set; }
            public double P95FrameTimeMs { get; private set; }
            public double P99FrameTimeMs { get; private set; }
            public double MaxFrameTimeMs { get; private set; }
            public double AverageFps { get; private set; }
            public double AverageGcAllocBytes => FrameCount > 0 ? (double)_totalGcAllocBytes / FrameCount : 0;
            public long MaxGcAllocBytes { get; private set; }
            public double? AverageUsedMemoryBytes => _usedMemoryBytes.Average;
            public double? MaxUsedMemoryBytes => _usedMemoryBytes.Maximum;
            public double? AverageBatches => _batches.Average;
            public double? AverageSetPassCalls => _setPassCalls.Average;
            public double? AverageDrawCalls => _drawCalls.Average;
            public double? AverageTriangles => _triangles.Average;

            public void AddFrame(
                int frameIndex,
                double frameTimeMs,
                double gpuTimeMs,
                long gcAllocBytes,
                long? usedMemoryBytes,
                long? batches,
                long? setPassCalls,
                long? drawCalls,
                long? triangles)
            {
                if (frameTimeMs <= 0)
                {
                    return;
                }

                _frameTimesMs.Add(frameTimeMs);
                _totalGcAllocBytes += gcAllocBytes;
                MaxGcAllocBytes = Math.Max(MaxGcAllocBytes, gcAllocBytes);
                _usedMemoryBytes.Add(usedMemoryBytes);
                _batches.Add(batches);
                _setPassCalls.Add(setPassCalls);
                _drawCalls.Add(drawCalls);
                _triangles.Add(triangles);
            }

            public void Calculate()
            {
                if (FrameCount == 0)
                {
                    return;
                }

                _frameTimesMs.Sort();
                double totalFrameTimeMs = _frameTimesMs.Sum();
                DurationSeconds = totalFrameTimeMs / 1000d;
                AverageFrameTimeMs = totalFrameTimeMs / FrameCount;
                P95FrameTimeMs = GetPercentile(_frameTimesMs, 0.95d);
                P99FrameTimeMs = GetPercentile(_frameTimesMs, 0.99d);
                MaxFrameTimeMs = _frameTimesMs[FrameCount - 1];
                AverageFps = 1000d / AverageFrameTimeMs;
            }

            private static double GetPercentile(IReadOnlyList<double> sortedValues, double percentile)
            {
                int index = Math.Max(0, (int)Math.Ceiling(percentile * sortedValues.Count) - 1);
                return sortedValues[index];
            }
        }

        private sealed class NumericAccumulator
        {
            private double _sum;
            private double _maximum;
            private int _count;

            public double? Average => _count > 0 ? _sum / _count : null;
            public double? Maximum => _count > 0 ? _maximum : null;

            public void Add(long? value)
            {
                if (!value.HasValue)
                {
                    return;
                }

                _sum += value.Value;
                _maximum = _count == 0 ? value.Value : Math.Max(_maximum, value.Value);
                _count++;
            }
        }
    }
}
