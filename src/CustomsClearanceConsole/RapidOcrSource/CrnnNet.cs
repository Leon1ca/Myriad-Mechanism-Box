using Emgu.CV;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RapidOCRLib.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RapidOCRLib
{
    class CrnnNet : IDisposable
    {
        private readonly float[] MeanValues = { 127.5F, 127.5F, 127.5F };
        private readonly float[] NormValues = { 1.0F / 127.5F, 1.0F / 127.5F, 1.0F / 127.5F };
        private const int crnnDstHeight = 48;
        private const int crnnCols = 6625;

        private InferenceSession crnnNet;
        private List<string> keys;
        private List<string> inputNames;

        public CrnnNet() { }

        public void Dispose()
        {
            crnnNet?.Dispose();
            crnnNet = null!;
        }

        public async Task InitModel(string path, string keysPath, int numThread)
        {
            try
            {
                using SessionOptions op = new SessionOptions();
                op.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
                op.InterOpNumThreads = numThread;
                op.IntraOpNumThreads = numThread;
                crnnNet = new InferenceSession(path, op);
                inputNames = crnnNet.InputMetadata.Keys.ToList();
                keys = InitKeys(keysPath);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message + ex.StackTrace);
                throw;
            }
        }
        private List<string> InitKeys(string path)
        {
            using StreamReader sr = new StreamReader(path, Encoding.UTF8);
            List<string> keys = new List<string>();
            keys.Add("#");
            String line;
            while ((line = sr.ReadLine()) != null)
            {
                //System.Diagnostics.Debug.WriteLine(line.ToString());
                keys.Add(line);
            }
            keys.Add(" ");
            return keys;
        }

        public List<TextLine> GetTextLines(List<Mat> partImgs)
        {
            List<TextLine> textLines = new List<TextLine>();
            for (int i = 0; i < partImgs.Count; i++)
            {
                var startTicks = DateTime.Now.Ticks;
                var textLine = GetTextLine(partImgs[i]);
                var endTicks = DateTime.Now.Ticks;
                var crnnTime = (endTicks - startTicks) / 10000F;
                textLine.Time = crnnTime;
                textLines.Add(textLine);
            }
            return textLines;
        }

        private TextLine GetTextLine(Mat src)
        {
            TextLine textLine = new TextLine();

            float scale = (float)crnnDstHeight / (float)src.Rows;
            int dstWidth = Math.Max(8, (int)((float)src.Cols * scale));

            using Mat srcResize = new Mat();
            CvInvoke.Resize(src, srcResize, new Size(dstWidth, crnnDstHeight));
            Tensor<float> inputTensors = OcrUtils.SubstractMeanNormalize(srcResize, MeanValues, NormValues);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputNames[0], inputTensors)
            };
            try
            {
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = crnnNet.Run(inputs))
                {
                    var resultsArray = results.ToArray();
                    var dimensions = resultsArray[0].AsTensor<float>().Dimensions;
                    float[] outputData = resultsArray[0].AsEnumerable<float>().ToArray();

                    return ScoreToTextLine(outputData, dimensions[1], dimensions[2]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message + ex.StackTrace);
                throw;
            }

        }

        private TextLine ScoreToTextLine(float[] srcData, int h, int w)
        {
            StringBuilder sb = new StringBuilder();
            TextLine textLine = new TextLine();

            int lastIndex = 0;
            List<float> scores = new List<float>();

            for (int i = 0; i < h; i++)
            {
                int maxIndex = 0;
                float maxValue = -1000F;
                for (int j = 0; j < w; j++)
                {
                    int idx = i * w + j;
                    if (srcData[idx] > maxValue)
                    {
                        maxIndex = j;
                        maxValue = srcData[idx];
                    }
                }

                if (maxIndex > 0 && maxIndex < keys.Count && (!(i > 0 && maxIndex == lastIndex)))
                {
                    scores.Add(maxValue);
                    sb.Append(keys[maxIndex]);
                }
                lastIndex = maxIndex;
            }
            textLine.Text = sb.ToString();
            textLine.CharScores = scores;
            return textLine;
        }

    }
}
