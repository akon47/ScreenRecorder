#pragma once

using namespace System;
using namespace System::Collections::Generic;

#include "PixelFormat.h"

namespace MediaEncoder {

	public ref class MediaFormat
	{
	public:
		static array<String^>^ GetAllFormatLongNames()
		{
			List<String^>^ ret = gcnew List<String^>();
			const AVOutputFormat* fmt = NULL;
			void* i = 0;
			//while ((fmt = av_oformat_next(fmt)))
			while ((fmt = av_muxer_iterate(&i)))
			{
				ret->Add(gcnew String(fmt->long_name));
				ret->Add(gcnew String(fmt->name));
			}
			return ret->Count > 0 ? ret->ToArray() : nullptr;
		}

		static String^ GetFormatExtensions(String^ format)
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

		static String^ GetFormatLongName(String^ format)
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

		static void GetFormatInfo(String^ format, [Runtime::InteropServices::Out] String^% longName, [Runtime::InteropServices::Out] String^% extensions)
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
	};

}