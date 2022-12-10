using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace ScreenRecorder.DirectX.Shader.Filter
{
    public class FlipFilterShader : NotifyPropertyBase, IFilterShader
    {
        #region Constructor

        public FlipFilterShader(Device device)
        {
            InitializeShader(device);
        }

        #endregion

        #region Properties

        public bool Enabled => true;

        private bool _verticalFlip;

        public bool VerticalFlip
        {
            get => _verticalFlip;
            set
            {
                lock (_syncObject)
                {
                    SetProperty(ref _verticalFlip, value);
                }
            }
        }

        private bool _horizontalFlip;

        public bool HorizontalFlip
        {
            get => _horizontalFlip;
            set
            {
                lock (_syncObject)
                {
                    SetProperty(ref _horizontalFlip, value);
                }
            }
        }

        #endregion

        #region Private Members

        private readonly string _shaderCode =
            @"
Texture2D Texture : register(t0);
SamplerState TextureSampler;

cbuffer Args
{
	bool h_flip;
	bool v_flip;
};

struct VSInput
{
	float4 position : POSITION;
	float2 uv : TEXCOORD0;
};

struct PSInput
{
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD0;
};

PSInput VShader(VSInput input)
{
	PSInput output;
	output.position = input.position;
	output.uv = input.uv;
	return output;
}

float4 SampleTexture(float2 uv)
{
	return Texture.Sample(TextureSampler, uv);
}

float4 PShader(PSInput input) : SV_Target
{
	float2 uv = input.uv;

	if(h_flip)
	{
		uv.x = 1.0 - input.uv.x;
	}
	if(v_flip)
	{
		uv.y = 1.0 - input.uv.y;
	}

	float4 rgba = Texture.Sample(TextureSampler, uv);
	return rgba;
}
";

        private InputLayout _inputLayout;
        private ShaderSignature _inputSignature;
        private VertexShader _vertexShader;
        private PixelShader _pixelShader;
        private SamplerState _samplerState;
        private Buffer _argsBuffer;
        private readonly object _syncObject = new object();

        #endregion

        #region Private Methods

        private void InitializeShader(Device device)
        {
            using (var bytecode = ShaderBytecode.Compile(_shaderCode, "VShader", "vs_4_0"))
            {
                _inputSignature = ShaderSignature.GetInputSignature(bytecode);
                _vertexShader = new VertexShader(device, bytecode);
            }

            using (var bytecode = ShaderBytecode.Compile(_shaderCode, "PShader", "ps_4_0"))
            {
                _pixelShader = new PixelShader(device, bytecode);
            }

            var elements = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, InputElement.AppendAligned, 0,
                    InputClassification.PerVertexData, 0)
            };

            _inputLayout = new InputLayout(device, _inputSignature, elements);

            _argsBuffer = new Buffer(device, 16, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write,
                ResourceOptionFlags.None, 0);

            _samplerState = new SamplerState(device,
                new SamplerStateDescription
                {
                    Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Wrap,
                    MipLodBias = 0.0f,
                    MaximumAnisotropy = 2,
                    ComparisonFunction = Comparison.Always,
                    BorderColor = new RawColor4(0, 0, 0, 0),
                    MinimumLod = 0,
                    MaximumLod = float.MaxValue
                });
        }

        public void Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView, float textureWidth,
            float textureHeight,
            bool hFlip, bool vFlip)
        {
            SetShaderParameters(deviceContext, shaderResourceView, textureWidth, textureHeight, hFlip, vFlip);
            RenderShader(deviceContext);

            if (shaderResourceView != null)
            {
                deviceContext.PixelShader.SetShaderResource(0, null);
            }
        }


        private bool _oldHFlip, _oldVFlip;

        private void SetShaderParameters(DeviceContext deviceContext, ShaderResourceView shaderResourceView,
            float textureWidth, float textureHeight,
            bool hFlip, bool vFlip)
        {
            if (hFlip != _oldHFlip || vFlip != _oldVFlip)
            {
                _oldHFlip = hFlip;
                _oldVFlip = vFlip;

                deviceContext.MapSubresource(_argsBuffer, MapMode.WriteDiscard, MapFlags.None, out var stream);
                stream.Write(hFlip ? 1 : 0);
                stream.Write(vFlip ? 1 : 0);
                deviceContext.UnmapSubresource(_argsBuffer, 0);
            }

            deviceContext.PixelShader.SetConstantBuffer(0, _argsBuffer);
            deviceContext.PixelShader.SetShaderResource(0, shaderResourceView);
        }

        private void RenderShader(DeviceContext deviceContext)
        {
            deviceContext.InputAssembler.InputLayout = _inputLayout;
            deviceContext.VertexShader.Set(_vertexShader);
            deviceContext.PixelShader.Set(_pixelShader);
            deviceContext.PixelShader.SetSampler(0, _samplerState);
            deviceContext.Draw(6, 0);
        }

        #endregion

        #region Public Methods

        public bool Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView, int resourceWidth,
            int resourceHeight)
        {
            lock (_syncObject)
            {
                if (_horizontalFlip || _verticalFlip)
                {
                    Render(deviceContext, shaderResourceView, resourceWidth, resourceHeight, _horizontalFlip,
                        _verticalFlip);
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (_inputLayout != null)
            {
                _inputLayout.Dispose();
            }

            if (_inputSignature != null)
            {
                _inputSignature.Dispose();
            }

            if (_vertexShader != null)
            {
                _vertexShader.Dispose();
            }

            if (_pixelShader != null)
            {
                _pixelShader.Dispose();
            }

            if (_samplerState != null)
            {
                _samplerState.Dispose();
            }

            if (_argsBuffer != null)
            {
                _argsBuffer.Dispose();
            }
        }

        #endregion
    }
}
