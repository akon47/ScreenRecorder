using System;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace ScreenRecorder.DirectX.Shader.Filter
{
    /// <summary>
    ///     ChromaKeyFilterShader
    ///     I used the following code ->
    ///     https://github.com/obsproject/obs-studio/blob/master/plugins/obs-filters/data/chroma_key_filter_v2.effect
    ///     I wrote the HLSL referring to the code above.
    /// </summary>
    public class ChromaKeyFilterShader : IDisposable
    {
        private readonly string _shaderCode =
            @"
Texture2D Texture : register(t0);
SamplerState TextureSampler;
cbuffer Args
{
	float opacity;
	float contrast;
	float brightness;
	float gamma;
	float2 chroma_key;
	float2 pixel_size;
	float similarity;
	float smoothness;
	float spill;
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
static const float4 cb_v4 = { -0.100644, -0.338572,  0.439216, 0.501961 };
static const float4 cr_v4 = {  0.439216, -0.398942, -0.040274, 0.501961 };
float4 CalcColor(float4 rgba)
{
	return float4(pow(rgba.rgb, float3(gamma, gamma, gamma)) * contrast + brightness, rgba.a);
}
float GetChromaDist(float3 rgb)
{
	float cb = dot(rgb.rgb, cb_v4.xyz) + cb_v4.w;
	float cr = dot(rgb.rgb, cr_v4.xyz) + cr_v4.w;
	return distance(chroma_key, float2(cr, cb));
}
float4 SampleTexture(float2 uv)
{
	return Texture.Sample(TextureSampler, uv);
}
float GetBoxFilteredChromaDist(float3 rgb, float2 texCoord)
{
	float2 h_pixel_size = pixel_size / 2.0;
	float2 point_0 = float2(pixel_size.x, h_pixel_size.y);
	float2 point_1 = float2(h_pixel_size.x, -pixel_size.y);
	float distVal = GetChromaDist(SampleTexture(texCoord-point_0).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord+point_0).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord-point_1).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord+point_1).rgb);
	distVal *= 2.0;
	distVal += GetChromaDist(rgb);
	return distVal / 9.0;
}
float4 ProcessChromaKey(float4 rgba, PSInput p_in)
{
	float chromaDist = GetBoxFilteredChromaDist(rgba.rgb, p_in.uv);
	float baseMask = chromaDist - similarity;
	float fullMask = pow(saturate(baseMask / smoothness), 1.5);
	float spillVal = pow(saturate(baseMask / spill), 1.5);
	rgba.a *= opacity;
	rgba.a *= fullMask;
	float desat = dot(rgba.rgb, float3(0.2126, 0.7152, 0.0722));
	rgba.rgb = lerp(float3(desat, desat, desat), rgba.rgb, spillVal);
	return CalcColor(rgba);
}
float4 PShader(PSInput input) : SV_Target
{
	float4 rgba = Texture.Sample(TextureSampler, input.uv);
	return ProcessChromaKey(rgba, input);
}
";

        private Buffer _argsBuffer;
        private InputLayout _inputLayout;
        private ShaderSignature _inputSignature;
        private int _oldKeyRed = -1, _oldKeyGreen = -1, _oldKeyBlue = -1;

        private float _oldOpacity = -1,
            _oldContrast = -1,
            _oldBrightness = -1,
            _oldGamma = -1,
            _oldSimilarity = -1,
            _oldSmoothness = -1,
            _oldSpill = -1;

        private PixelShader _pixelShader;
        private SamplerState _samplerState;
        private VertexShader _vertexShader;

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

        public void Initialize(Device device)
        {
            InitializeShader(device);
        }

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

            _argsBuffer = new Buffer(device, 64, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write,
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
            float opacity, float contrast, float brightness, float gamma, int keyRed, int keyGreen, int keyBlue,
            float similarity, float smoothness, float spill)
        {
            SetShaderParameters(deviceContext, shaderResourceView, textureWidth, textureHeight, opacity, contrast,
                brightness, gamma, keyRed, keyGreen, keyBlue, similarity, smoothness, spill);
            RenderShader(deviceContext);
        }

        private static float srgb_nonlinear_to_linear(float u)
        {
            return (float)(u <= 0.04045d ? u / 12.92d : Math.Pow((u + 0.055d) / 1.055d, 2.4d));
        }

        private static void vec4_from_rgba_srgb(out Vector4 dst, uint rgba)
        {
            dst = new Vector4();
            dst.X = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.Y = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.Z = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.W = (rgba & 0xFF) / 255.0f;
        }

        private void SetShaderParameters(DeviceContext deviceContext, ShaderResourceView shaderResourceView,
            float textureWidth, float textureHeight,
            float opacity, float contrast, float brightness, float gamma, int keyRed, int keyGreen, int keyBlue,
            float similarity, float smoothness, float spill)
        {
            if (opacity != _oldOpacity || contrast != _oldContrast || brightness != _oldBrightness || gamma != _oldGamma ||
                keyRed != _oldKeyRed || keyGreen != _oldKeyGreen || keyBlue != _oldKeyBlue ||
                similarity != _oldSimilarity || smoothness != _oldSmoothness || spill != _oldSpill)
            {
                _oldOpacity = opacity;
                _oldContrast = contrast;
                _oldBrightness = brightness;
                _oldGamma = gamma;
                _oldSimilarity = similarity;
                _oldSmoothness = smoothness;
                _oldSpill = spill;

                if (keyRed < 0)
                {
                    keyRed = 0;
                }
                else if (keyRed > 255)
                {
                    keyRed = 255;
                }

                if (keyGreen < 0)
                {
                    keyGreen = 0;
                }
                else if (keyGreen > 255)
                {
                    keyGreen = 255;
                }

                if (keyBlue < 0)
                {
                    keyBlue = 0;
                }
                else if (keyBlue > 255)
                {
                    keyBlue = 255;
                }

                _oldKeyRed = keyRed;
                _oldKeyGreen = keyGreen;
                _oldKeyBlue = keyBlue;

                var keyColor = ((uint)keyRed << 16) | ((uint)keyGreen << 8) | ((uint)keyBlue << 0);
                vec4_from_rgba_srgb(out var keyRgb, keyColor | 0xFF000000);
                var cb = new Vector4(-0.100644f, -0.338572f, 0.439216f, 0.501961f);
                var cr = new Vector4(0.439216f, -0.398942f, -0.040274f, 0.501961f);
                deviceContext.MapSubresource(_argsBuffer, MapMode.WriteDiscard, MapFlags.None, out var stream);
                stream.Write(opacity);
                stream.Write(contrast);
                stream.Write(brightness);
                stream.Write(gamma);
                stream.Write(new Vector2(Vector4.Dot(keyRgb, cb), Vector4.Dot(keyRgb, cr)));
                stream.Write(new Vector2(1.0f / textureWidth, 1.0f / textureHeight));
                stream.Write(similarity);
                stream.Write(smoothness);
                stream.Write(spill);
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
    }
}
