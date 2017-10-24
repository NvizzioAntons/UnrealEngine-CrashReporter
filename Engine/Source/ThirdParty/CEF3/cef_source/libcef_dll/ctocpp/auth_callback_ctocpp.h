// Copyright (c) 2017 The Chromium Embedded Framework Authors. All rights
// reserved. Use of this source code is governed by a BSD-style license that
// can be found in the LICENSE file.
//
// ---------------------------------------------------------------------------
//
// This file was generated by the CEF translator tool. If making changes by
// hand only do so within the body of existing method and function
// implementations. See the translator.README.txt file in the tools directory
// for more information.
//

#ifndef CEF_LIBCEF_DLL_CTOCPP_AUTH_CALLBACK_CTOCPP_H_
#define CEF_LIBCEF_DLL_CTOCPP_AUTH_CALLBACK_CTOCPP_H_
#pragma once

#if !defined(WRAPPING_CEF_SHARED)
#error This file can be included wrapper-side only
#endif

#include "include/cef_auth_callback.h"
#include "include/capi/cef_auth_callback_capi.h"
#include "libcef_dll/ctocpp/ctocpp_ref_counted.h"

// Wrap a C structure with a C++ class.
// This class may be instantiated and accessed wrapper-side only.
class CefAuthCallbackCToCpp
    : public CefCToCppRefCounted<CefAuthCallbackCToCpp, CefAuthCallback,
        cef_auth_callback_t> {
 public:
  CefAuthCallbackCToCpp();

  // CefAuthCallback methods.
  void Continue(const CefString& username, const CefString& password) OVERRIDE;
  void Cancel() OVERRIDE;
};

#endif  // CEF_LIBCEF_DLL_CTOCPP_AUTH_CALLBACK_CTOCPP_H_
