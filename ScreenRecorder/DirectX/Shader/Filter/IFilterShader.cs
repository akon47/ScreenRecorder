using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.DirectX.Shader.Filter
{
    public interface IFilterShader : IDisposable
    {
        bool Enabled { get; }
        bool Render(SharpDX.Direct3D11.DeviceContext deviceContext, SharpDX.Direct3D11.ShaderResourceView shaderResourceView, int resourceWidth, int resourceHeight);
    }
}
