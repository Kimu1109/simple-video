using System;
using SkiaSharp;

namespace SimpleVideo.Core.Rendering;

public interface IRenderer
{
    SKBitmap RenderFrame(TimeSpan time);
}
