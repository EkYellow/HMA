﻿using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;

namespace HavenSoft.HexManiac.Core.ViewModels.Images {
   public class CanvasPixelViewModel : ViewModelCore, IPixelViewModel {
      public short Transparent { get; init; } = -1;
      public int PixelWidth { get; }
      public int PixelHeight { get; }
      public short[] PixelData { get; private set; }

      private double spriteScale = 1;
      public double SpriteScale {
         get => spriteScale;
         set => Set(ref spriteScale, value, old => {
            NotifyPropertyChanged(nameof(ScaledWidth));
            NotifyPropertyChanged(nameof(ScaledHeight));
         });
      }

      public double ScaledWidth => PixelWidth * SpriteScale;
      public double ScaledHeight => PixelHeight * SpriteScale;

      public CanvasPixelViewModel(int width, int height, short[] data = null) {
         (PixelWidth, PixelHeight) = (width, height);
         PixelData = data ?? new short[width * height];
      }

      public void Fill(short[] pixelData) {
         if (pixelData.Length != PixelData.Length) throw new NotSupportedException($"Need {PixelData.Length} pixels to fill, but was given {pixelData.Length} pixels.");
         PixelData = pixelData;
         NotifyPropertyChanged(nameof(PixelData));
      }

      public void Draw(IPixelViewModel foreground, int x, int y) {
         for (int yy = 0; yy < foreground.PixelHeight; yy++) {
            for (int xx = 0; xx < foreground.PixelWidth; xx++) {
               var pixel = foreground.PixelData[foreground.PixelWidth * yy + xx];
               if (pixel == foreground.Transparent) continue;
               if (x + xx >= PixelWidth || y + yy >= PixelHeight) continue;
               if (x + xx < 0 || y + yy < 0) continue;
               int offset = PixelWidth * (y + yy) + (x + xx);
               PixelData[offset] = pixel;
            }
         }
         NotifyPropertyChanged(nameof(PixelData));
      }

      public void DrawBox(int x, int y, int size, short color) => DrawRect(x, y, size, size, color);

      public void DrawRect(int x, int y, int width, int height, short color) {
         for (int i = 0; i < width - 1; i++) {
            PixelData[x + i + y * PixelWidth] = color;
            PixelData[x + width - 1 - i + (y + height - 1) * PixelWidth] = color;
         }
         for (int i = 0; i < height - 1; i++) {
            PixelData[x + (y + height - 1 - i) * PixelWidth] = color;
            PixelData[x + width - 1 + (y + i) * PixelWidth] = color;
         }
      }

      public void DarkenRect(int x, int y, int width, int height, int darkness) {
         for (int i = 0; i < width - 1; i++) {
            var (p1, p2) = (x + i + y * PixelWidth, x + width - 1 - i + (y + height - 1) * PixelWidth);
            PixelData[p1] = Darken(PixelData[p1], darkness);
            PixelData[p2] = Darken(PixelData[p2], darkness);
         }
         for (int i = 0; i < height - 1; i++) {
            var (p1, p2) = (x + (y + height - 1 - i) * PixelWidth, x + width - 1 + (y + i) * PixelWidth);
            PixelData[p1] = Darken(PixelData[p1], darkness);
            PixelData[p2] = Darken(PixelData[p2], darkness);
         }
      }

      public static short Darken(short color, int amount) {
         var rgb = UncompressedPaletteColor.ToRGB(color);
         rgb.r = (rgb.r - amount).LimitToRange(0, 31);
         rgb.g = (rgb.g - amount).LimitToRange(0, 31);
         rgb.b = (rgb.b - amount).LimitToRange(0, 31);
         return UncompressedPaletteColor.Pack(rgb.r, rgb.g, rgb.b);
      }
   }
}
