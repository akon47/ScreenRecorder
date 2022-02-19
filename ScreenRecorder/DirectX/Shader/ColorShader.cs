using System;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;

namespace ScreenRecorder.DirectX.Shader
{
    public class ColorShader : IDisposable
    {
        private readonly string shaderCode =
            @"
Texture2D Texture : register(t0);
SamplerState TextureSampler;

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

float4 PShader(PSInput input) : SV_Target
{
	float4 color = Texture.Sample(TextureSampler, input.uv);
	return color;
}
";

        private InputLayout inputLayout;
        private ShaderSignature inputSignature;
        private PixelShader pixelShader;
        private SamplerState samplerState;
        private VertexShader vertexShader;

        public void Dispose()
        {
            inputLayout?.Dispose();
            inputSignature?.Dispose();
            vertexShader?.Dispose();
            pixelShader?.Dispose();
            samplerState?.Dispose();
        }

        public void Initialize(Device device)
        {
            InitializeShader(device);
        }

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

            samplerState = new SamplerState(device,
                new SamplerStateDescription
                {
                    Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Wrap,
                    AddressV = TextureAddressMode.Wrap,
                    AddressW = TextureAddressMode.Wrap,
                    MipLodBias = 0.0f,
                    MaximumAnisotropy = 1,
                    ComparisonFunction = Comparison.Always,
                    BorderColor = new RawColor4(0, 0, 0, 0),
                    MinimumLod = 0,
                    MaximumLod = float.MaxValue
                });
        }

        public void Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView)
        {
            SetShaderParameters(deviceContext, shaderResourceView);
            RenderShader(deviceContext);

            if (shaderResourceView != null)
            {
                deviceContext.PixelShader.SetShaderResource(0, null);
            }
        }

        private void SetShaderParameters(DeviceContext deviceContext, ShaderResourceView shaderResourceView)
        {
            deviceContext.PixelShader.SetConstantBuffer(0, null);
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
    }
}
