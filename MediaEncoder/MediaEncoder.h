#pragma once

using namespace System;
using namespace Collections::Generic;

#include "PixelFormat.h"

namespace MediaEncoder
{
	public ref class MediaFormat
	{
	public:
		static array<String^>^ GetAllFormatLongNames();
		static String^ GetFormatExtensions(String^ format);
		static String^ GetFormatLongName(String^ format);
		static void GetFormatInfo(String^ format, [Runtime::InteropServices::Out] String^% longName,
		                          [Runtime::InteropServices::Out] String^% extensions);
	};
}
