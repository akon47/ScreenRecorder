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

        public bool verticalFlip;

        public bool VerticalFlip
        {
            get => verticalFlip;
            set
            {
                lock (syncObject)
                {
                    SetProperty(ref verticalFlip, value);
                }
            }
        }

        public bool horizontalFlip;

        public bool HorizontalFlip
        {
            get => horizontalFlip;
            set
            {
                lock (syncObject)
                {
                    SetProperty(ref horizontalFlip, value);
                }
            }
        }

        #endregion

        #region Private Members

        private readonly string shaderCode =
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

        private InputLayout inputLayout;
        private ShaderSignature inputSignature;
        private VertexShader vertexShader;
        private PixelShader pixelShader;
        private SamplerState samplerState;
        private Buffer argsBuffer;
        private readonly object syncObject = new object();

        #endregion

        #region Private Methods

        private void InitializeShader(Device device)
        {
            using (var bytecode = ShaderBytecode.Compile(shaderCode, "VShader", "vs_4_0"))
            {
                inputSignature = ShaderSignature.GetInputSignature(bytecode);
                vertexShader = new VertexShader(device, bytecode);
            }

            using (var bytecode = ShaderBytecode.Compile(shaderCode, "PShader", "ps_4_0"))
            {
                pixelShader = new PixelShader(device, bytecode);
            }

            var elements = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, InputElement.AppendAligned, 0,
                    InputClassification.PerVertexData, 0)
            };

            inputLayout = new InputLayout(device, inputSignature, elements);

            argsBuffer = new Buffer(device, 16, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write,
                ResourceOptionFlags.None, 0);

            samplerState = new SamplerState(device,
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
            bool h_flip, bool v_flip)
        {
            SetShaderParameters(deviceContext, shaderResourceView, textureWidth, textureHeight, h_flip, v_flip);
            RenderShader(deviceContext);

            if (shaderResourceView != null)
            {
                deviceContext.PixelShader.SetShaderResource(0, null);
            }
        }


        private bool oldHFlip, oldVFlip;

        private void SetShaderParameters(DeviceContext deviceContext, ShaderResourceView shaderResourceView,
            float textureWidth, float textureHeight,
            bool h_flip, bool v_flip)
        {
            if (h_flip != oldHFlip || v_flip != oldVFlip)
            {
                oldHFlip = h_flip;
                oldVFlip = v_flip;

                deviceContext.MapSubresource(argsBuffer, MapMode.WriteDiscard, MapFlags.None, out var stream);
                stream.Write(h_flip ? 1 : 0);
                stream.Write(v_flip ? 1 : 0);
                deviceContext.UnmapSubresource(argsBuffer, 0);
            }

            deviceContext.PixelShader.SetConstantBuffer(0, argsBuffer);
            deviceContext.PixelShader.SetShaderResource(0, shaderResourceView);
        }

        private void RenderShader(DeviceContext deviceContext)
        {
            deviceContext.InputAssembler.InputLayout = inputLayout;
            deviceContext.VertexShader.Set(vertexShader);
            deviceContext.PixelShader.Set(pixelShader);
            deviceContext.PixelShader.SetSampler(0, samplerState);
            deviceContext.Draw(6, 0);
        }

        #endregion

        #region Public Methods

        public bool Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView, int resourceWidth,
            int resourceHeight)
        {
            lock (syncObject)
            {
                if (horizontalFlip || verticalFlip)
                {
                    Render(deviceContext, shaderResourceView, resourceWidth, resourceHeight, horizontalFlip,
                        verticalFlip);
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (inputLayout != null)
            {
                inputLayout.Dispose();
            }

            if (inputSignature != null)
            {
                inputSignature.Dispose();
            }

            if (vertexShader != null)
            {
                vertexShader.Dispose();
            }

            if (pixelShader != null)
            {
                pixelShader.Dispose();
            }

            if (samplerState != null)
            {
                samplerState.Dispose();
            }

            if (argsBuffer != null)
            {
                argsBuffer.Dispose();
            }
        }

        #endregion
    }
}
