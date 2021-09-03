#include "pch.h"
#include "MediaEncoder.h"

namespace MediaEncoder {

	array<String^>^ MediaFormat::GetAllFormatLongNames()
	{
		List<String^>^ ret = gcnew List<String^>();
		const AVOutputFormat* fmt = NULL;
		void* i = 0;
		while ((fmt = av_muxer_iterate(&i)))
		{
			ret->Add(gcnew String(fmt->long_name));
			ret->Add(gcnew String(fmt->name));
		}
		return ret->Count > 0 ? ret->ToArray() : nullptr;
	}

	String^ MediaFormat::GetFormatExtensions(String^ format)
	{
		IntPtr formatStringPointer = System::Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		wchar_t* nativeFormatUnicode = (wchar_t*)formatStringPointer.ToPointer();
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, NULL, 0, NULL, NULL);
		char* nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, NULL, NULL);

		const AVOutputFormat* fmt = av_guess_format(nativeFormat, nullptr, nullptr);
		if (fmt != nullptr)
			return gcnew String(fmt->extensions);
		else
			return nullptr;
	}

	String^ MediaFormat::GetFormatLongName(String^ format)
	{
		IntPtr formatStringPointer = System::Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		wchar_t* nativeFormatUnicode = (wchar_t*)formatStringPointer.ToPointer();
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, NULL, 0, NULL, NULL);
		char* nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, NULL, NULL);
		const AVOutputFormat* fmt = av_guess_format(nativeFormat, nullptr, nullptr);
		if (fmt != nullptr)
			return gcnew String(fmt->long_name);
		else
			return nullptr;
	}

	void MediaFormat::GetFormatInfo(String^ format, [Runtime::InteropServices::Out] String^% longName, [Runtime::InteropServices::Out] String^% extensions)
	{
		IntPtr formatStringPointer = System::Runtime::InteropServices::Marshal::StringToHGlobalUni(format);
		wchar_t* nativeFormatUnicode = (wchar_t*)formatStringPointer.ToPointer();
		int utf8FormatStringSize = WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, NULL, 0, NULL, NULL);
		char* nativeFormat = new char[utf8FormatStringSize];
		WideCharToMultiByte(CP_UTF8, 0, nativeFormatUnicode, -1, nativeFormat, utf8FormatStringSize, NULL, NULL);
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