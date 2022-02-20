#include "pch.h"
#include "MediaEncoder.h"

namespace MediaEncoder
{
	array<String^>^ MediaFormat::GetAllFormatLongNames()
	{
		auto ret = gcnew List<String^>();
		const AVOutputFormat* fmt = nullptr;
		void* i = nullptr;
		while ((fmt = av_muxer_iterate(&i)))
		{
			ret->Add(gcnew String(fmt->long_name));
			ret->Add(gcnew String(fmt->name));
		}
		return ret->Count > 0 ? ret->ToArray() : nullptr;
	}

	String^ MediaFormat::GetFormatExtensions(String^ format)
	{
		IntPtr formatStringPointer = Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		auto nativeFormatUnicode = static_cast<wchar_t*>(formatStringPointer.ToPointer());
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nullptr, 0, nullptr,
		                                               nullptr);
		auto nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, nullptr, nullptr);

		const AVOutputFormat* fmt = av_guess_format(nativeFormat, nullptr, nullptr);
		if (fmt != nullptr)
			return gcnew String(fmt->extensions);
		return nullptr;
	}

	String^ MediaFormat::GetFormatLongName(String^ format)
	{
		IntPtr formatStringPointer = Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		auto nativeFormatUnicode = static_cast<wchar_t*>(formatStringPointer.ToPointer());
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nullptr, 0, nullptr,
		                                               nullptr);
		auto nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, nullptr, nullptr);
		const AVOutputFormat* fmt = av_guess_format(nativeFormat, nullptr, nullptr);
		if (fmt != nullptr)
			return gcnew String(fmt->long_name);
		return nullptr;
	}

	void MediaFormat::GetFormatInfo(String^ format, [Runtime::InteropServices::Out] String^% longName,
	                                [Runtime::InteropServices::Out] String^% extensions)
	{
		IntPtr formatStringPointer = Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		auto nativeFormatUnicode = static_cast<wchar_t*>(formatStringPointer.ToPointer());
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nullptr, 0, nullptr,
		                                               nullptr);
		auto nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, nullptr, nullptr);
		const AVOutputFormat* fmt = av_guess_format(nativeFormat, nullptr, nullptr);
		if (fmt != nullptr)
		{
			longName = gcnew String(fmt->long_name);
			extensions = gcnew String(fmt->extensions);
		}
		else
		{
			longName = nullptr;
			extensions = nullptr;
		}
	}
}
