using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.ML.OnnxRuntime.Tensors;
using RapidOCRLib.Models;
using TextBox = RapidOCRLib.Models.TextBox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace RapidOCRLib
{
    class OcrUtils
    {
        public static Tensor<float> SubstractMeanNormalize(Mat src, float[] meanVals, float[] normVals)
        {
            int cols = src.Cols;
            int rows = src.Rows;
            int channels = src.NumberOfChannels;
            Image<Rgb, byte> srcImg = src.ToImage<Rgb, byte>();
            byte[,,] imgData = srcImg.Data;
            Tensor<float> inputTensor = new DenseTensor<float>(new[] { 1, channels, rows, cols });
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        var value = imgData[r, c, ch];
                        float data = (float)(value * normVals[ch] - meanVals[ch] * normVals[ch]);
                        inputTensor[0, ch, r, c] = data;
                    }
                }
            }
            return inputTensor;
        }

        public static Mat MakePadding(Mat src, int padding)
        {
            if (padding <= 0) return src;
            MCvScalar paddingScalar = new MCvScalar(255, 255, 255);
            Mat paddingSrc = new Mat();
            CvInvoke.CopyMakeBorder(src, paddingSrc, padding, padding, padding, padding, BorderType.Isolated, paddingScalar);
            return paddingSrc;
        }

        public static int GetThickness(Mat boxImg)
        {
            int minSize = boxImg.Cols > boxImg.Rows ? boxImg.Rows : boxImg.Cols;
            int thickness = minSize / 1000 + 2;
            return thickness;
        }

        public static void DrawTextBox(Mat boxImg, List<Point> box, int thickness)
        {
            if (box == null || box.Count == 0)
            {
                return;
            }
            var color = new MCvScalar(0, 0, 255);//B(0) G(0) R(255)
            CvInvoke.Line(boxImg, box[0], box[1], color, thickness);
            CvInvoke.Line(boxImg, box[1], box[2], color, thickness);
            CvInvoke.Line(boxImg, box[2], box[3], color, thickness);
            CvInvoke.Line(boxImg, box[3], box[0], color, thickness);
        }

        public static void DrawTextBoxes(Mat src, List<TextBox> textBoxes, int thickness)
        {
            for (int i = 0; i < textBoxes.Count; i++)
            {
                TextBox t = textBoxes[i];
                DrawTextBox(src, t.Points, thickness);
            }
        }

        public static List<Mat> GetPartImages(Mat src, List<TextBox> textBoxes)
        {
            List<Mat> partImages = new List<Mat>();
            try
            {
            for (int i = 0; i < textBoxes.Count; ++i)
            {
                Mat partImg = GetRotateCropImage(src, textBoxes[i].Points);
                //using Mat partImg = new Mat();
                //GetRoiFromBox(src, partImg, textBoxes[i].Points);
                partImages.Add(partImg);
            }
            return partImages;
            }
            catch
            {
                foreach (var part in partImages) part.Dispose();
                throw;
            }
        }

        public static Mat GetRotateCropImage(Mat src, List<Point> box)
        {
            List<Point> points = new List<Point>();
            points.AddRange(box);

            int[] collectX = { box[0].X, box[1].X, box[2].X, box[3].X };
            int[] collectY = { box[0].Y, box[1].Y, box[2].Y, box[3].Y };
            int left = Math.Clamp(collectX.Min(), 0, Math.Max(0, src.Cols - 1));
            int right = Math.Clamp(collectX.Max(), left + 1, src.Cols);
            int top = Math.Clamp(collectY.Min(), 0, Math.Max(0, src.Rows - 1));
            int bottom = Math.Clamp(collectY.Max(), top + 1, src.Rows);

            Rectangle rect = new Rectangle(left, top, right - left, bottom - top);
            using Mat imgCrop = new Mat(src, rect);

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                pt.X -= left;
                pt.Y -= top;
                points[i] = pt;
            }

            int imgCropWidth = Math.Max(2, (int)(Math.Sqrt(Math.Pow(points[0].X - points[1].X, 2) +
                                        Math.Pow(points[0].Y - points[1].Y, 2))));
            int imgCropHeight = Math.Max(2, (int)(Math.Sqrt(Math.Pow(points[0].X - points[3].X, 2) +
                                         Math.Pow(points[0].Y - points[3].Y, 2))));

            var ptsDst0 = new PointF(0, 0);
            var ptsDst1 = new PointF(imgCropWidth, 0);
            var ptsDst2 = new PointF(imgCropWidth, imgCropHeight);
            var ptsDst3 = new PointF(0, imgCropHeight);

            PointF[] ptsDst = { ptsDst0, ptsDst1, ptsDst2, ptsDst3 };


            var ptsSrc0 = new PointF(points[0].X, points[0].Y);
            var ptsSrc1 = new PointF(points[1].X, points[1].Y);
            var ptsSrc2 = new PointF(points[2].X, points[2].Y);
            var ptsSrc3 = new PointF(points[3].X, points[3].Y);

            PointF[] ptsSrc = { ptsSrc0, ptsSrc1, ptsSrc2, ptsSrc3 };

            using Mat M = CvInvoke.GetPerspectiveTransform(ptsSrc, ptsDst);

            using Mat partImg = new Mat();
            CvInvoke.WarpPerspective(imgCrop, partImg, M,
                                new Size(imgCropWidth, imgCropHeight), Inter.Nearest, Warp.Default,
                               BorderType.Replicate);

            if (partImg.Rows >= partImg.Cols * 1.5)
            {
                using Mat srcCopy = new Mat();
                CvInvoke.Transpose(partImg, srcCopy);
                CvInvoke.Flip(srcCopy, srcCopy, 0);
                return srcCopy.Clone();
            }
            else
            {
                return partImg.Clone();
            }
        }

        public static Mat MatRotateClockWise180(Mat src)
        {
            CvInvoke.Flip(src, src, FlipType.Vertical);
            CvInvoke.Flip(src, src, FlipType.Horizontal);
            return src;
        }

        public static Mat MatRotateClockWise90(Mat src)
        {
            CvInvoke.Rotate(src, src, RotateFlags.Rotate90CounterClockwise);
            return src;
        }

    }
}
