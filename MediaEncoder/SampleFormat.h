#pragma once

#include "pch.h"

namespace MediaEncoder
{
	public enum class SampleFormat
	{
		NONE = AV_SAMPLE_FMT_NONE,
		U8 = AV_SAMPLE_FMT_U8,
		///< unsigned 8 bits
		S16 = AV_SAMPLE_FMT_S16,
		///< signed 16 bits
		S32 = AV_SAMPLE_FMT_S32,
		///< signed 32 bits
		FLT = AV_SAMPLE_FMT_FLT,
		///< float
		DBL = AV_SAMPLE_FMT_DBL,
		///< double

		U8P = AV_SAMPLE_FMT_U8P,
		///< unsigned 8 bits, planar
		S16P = AV_SAMPLE_FMT_S16P,
		///< signed 16 bits, planar
		S32P = AV_SAMPLE_FMT_S32P,
		///< signed 32 bits, planar
		FLTP = AV_SAMPLE_FMT_FLTP,
		///< float, planar
		DBLP = AV_SAMPLE_FMT_DBLP,
		///< double, planar
		S64 = AV_SAMPLE_FMT_S64,
		///< signed 64 bits
		S64P = AV_SAMPLE_FMT_S64P,
		///< signed 64 bits, planar
		NB = AV_SAMPLE_FMT_NB ///< Number of sample formats. DO NOT USE if linking dynamically
	};
}
