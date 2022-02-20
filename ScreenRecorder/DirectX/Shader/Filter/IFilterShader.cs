using System;
using SharpDX.Direct3D11;

namespace ScreenRecorder.DirectX.Shader.Filter
{
    public interface IFilterShader : IDisposable
    {
        bool Enabled { get; }

        bool Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView, int resourceWidth,
            int resourceHeight);
    }
}
