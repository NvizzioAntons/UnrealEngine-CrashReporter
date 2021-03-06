// Copyright 1998-2017 Epic Games, Inc. All Rights Reserved.

#pragma once

#include "ILiveLinkSource.h"
#include "MessageEndpoint.h"
#include "IMessageContext.h"

class ILiveLinkClient;
struct FLiveLinkPongMessage;
struct FLiveLinkSubjectDataMessage;
struct FLiveLinkSubjectFrameMessage;
struct FLiveLinkHeartbeatMessage;
struct FLiveLinkClearSubject;

class FLiveLinkMessageBusSource : public ILiveLinkSource
{
public:

	FLiveLinkMessageBusSource(const FText& InSourceType, const FText& InSourceMachineName, const FMessageAddress& InConnectionAddress)
		: ConnectionAddress(InConnectionAddress)
		, SourceType(InSourceType)
		, SourceMachineName(InSourceMachineName)
		, HeartbeatLastSent(0.0)
		, ConnectionLastActive(0.0)
	{}

	virtual void ReceiveClient(ILiveLinkClient* InClient, FGuid InSourceGuid);

	void Connect(FMessageAddress& Address);

	virtual bool IsSourceStillValid();

	virtual bool RequestSourceShutdown();

	virtual FText GetSourceType() const { return SourceType; }
	virtual FText GetSourceMachineName() const { return SourceMachineName; }
	virtual FText GetSourceStatus() const { return SourceStatus; }

private:

	// Message bus message handlers
	void HandleSubjectData(const FLiveLinkSubjectDataMessage& Message, const IMessageContextRef& Context);
	void HandleSubjectFrame(const FLiveLinkSubjectFrameMessage& Message, const IMessageContextRef& Context);
	void HandleHeartbeat(const FLiveLinkHeartbeatMessage& Message, const IMessageContextRef& Context);
	void HandleClearSubject(const FLiveLinkClearSubject& Message, const IMessageContextRef& Context);
	// End Message bus message handlers

	ILiveLinkClient* Client;

	// Our identifier in LiveLink
	FGuid SourceGuid;

	FMessageEndpointPtr MessageEndpoint;

	FMessageAddress ConnectionAddress;

	FText SourceType;
	FText SourceMachineName;
	FText SourceStatus;

	// Time we last sent connection heartbeat
	double HeartbeatLastSent;

	// Time we last recieved anything 
	double ConnectionLastActive;
};