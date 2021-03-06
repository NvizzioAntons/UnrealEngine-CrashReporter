// Copyright 1998-2017 Epic Games, Inc. All Rights Reserved.

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"
#include "Interfaces/ITargetDeviceServicesModule.h"
#include "TargetDeviceServicesLog.h"
#include "TargetDeviceProxyManager.h"
#include "TargetDeviceServiceManager.h"


DEFINE_LOG_CATEGORY(TargetDeviceServicesLog);


/**
 * Implements the TargetDeviceServices module.
 */
class FTargetDeviceServicesModule
	: public ITargetDeviceServicesModule
{
public:

	// ITargetDeviceServicesModule interface

	virtual ITargetDeviceProxyManagerRef GetDeviceProxyManager() override
	{
		if (!DeviceProxyManagerSingleton.IsValid())
		{
			DeviceProxyManagerSingleton = MakeShareable(new FTargetDeviceProxyManager());
		}

		return DeviceProxyManagerSingleton.ToSharedRef();
	}

	virtual ITargetDeviceServiceManagerRef GetDeviceServiceManager() override
	{
		if (!DeviceServiceManagerSingleton.IsValid())
		{
			DeviceServiceManagerSingleton = MakeShareable(new FTargetDeviceServiceManager());
		}

		return DeviceServiceManagerSingleton.ToSharedRef();
	}

private:

	/** Holds the device proxy manager singleton. */
	ITargetDeviceProxyManagerPtr DeviceProxyManagerSingleton;

	/** Holds the device service manager singleton. */
	ITargetDeviceServiceManagerPtr DeviceServiceManagerSingleton;
};


IMPLEMENT_MODULE(FTargetDeviceServicesModule, TargetDeviceServices);
