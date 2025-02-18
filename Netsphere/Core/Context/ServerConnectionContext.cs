// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Relay;

#pragma warning disable SA1202

namespace Netsphere;

internal class ExampleConnectionContext : ServerConnectionContext
{
    public ExampleConnectionContext(ServerConnection serverConnection)
        : base(serverConnection)
    {
    }

    /*public override bool RespondUpdateAgreement(CertificateToken<ConnectionAgreement> token)
    {// Accept all agreement.
        if (!this.ServerConnection.ValidateAndVerifyWithSalt(token))
        {
            return false;
        }

        return true;
    }

    public override bool RespondConnectBidirectionally(CertificateToken<ConnectionAgreement>? token)
    {// Enable bidirectional connection.
        if (token is null ||
            !this.ServerConnection.ValidateAndVerifyWithSalt(token))
        {
            return false;
        }

        return true;
    }*/
}

public class ServerConnectionContext
{
    #region Service

    public delegate INetService CreateFrontendDelegate(ClientConnection clientConnection);

    public ServerConnectionContext(ServerConnection serverConnection)
    {
        this.ServiceProvider = serverConnection.ConnectionTerminal.ServiceProvider;
        // this.serviceScope = serverConnection.ConnectionTerminal.ServiceProvider.CreateScope();
        this.NetTerminal = serverConnection.ConnectionTerminal.NetTerminal;
        this.ServerConnection = serverConnection;

        this.serviceTable = this.NetTerminal.Services.GetTable();
        this.agentInstances = new object[this.serviceTable.Count];
    }

    #endregion

    #region FieldAndProperty

    public IServiceProvider ServiceProvider { get; }

    public NetTerminal NetTerminal { get; }

    public ServerConnection ServerConnection { get; }

    public AuthenticationToken? AuthenticationToken { get; private set; }

    // private readonly IServiceScope serviceScope;
    private readonly ServiceControl.Table serviceTable;
    private readonly object[] agentInstances;

    #endregion

    /*public virtual bool RespondUpdateAgreement(CertificateToken<ConnectionAgreement> token)
        => false;

    public virtual bool RespondConnectBidirectionally(CertificateToken<ConnectionAgreement>? token)
        => false;*/

    /*public NetResult Authenticate(AuthenticationToken authenticationToken, SignaturePublicKey publicKey)
    {
        if (!authenticationToken.PublicKey.Equals(publicKey))
        {
            return NetResult.NotAuthenticated;
        }

        return this.Authenticate(authenticationToken);
    }

    public NetResult Authenticate(AuthenticationToken authenticationToken)
    {
        if (this.ServerConnection.ValidateAndVerifyWithSalt(authenticationToken))
        {
            this.AuthenticationToken = authenticationToken;
            return NetResult.Success;
        }
        else
        {
            return NetResult.NotAuthenticated;
        }
    }*/

    /*public bool TryGetAuthenticationToken([MaybeNullWhen(false)] out AuthenticationToken authenticationToken)
    {
        authenticationToken = this.AuthenticationToken;
        return authenticationToken is not null;
    }*/

    internal void InvokeStream(ReceiveTransmission receiveTransmission, ulong dataId, long maxStreamLength)
    {
        // Get ServiceMethod
        (var serviceMethod, var agentInstance) = this.TryGetServiceMethod(dataId);
        if (serviceMethod is null)
        {
            return;
        }

        var transmissionContext = new TransmissionContext(this.ServerConnection, receiveTransmission.TransmissionId, 1, dataId, default);
        if (!transmissionContext.CreateReceiveStream(receiveTransmission, maxStreamLength))
        {
            transmissionContext.ReturnAndDisposeStream();
            receiveTransmission.Dispose();
            return;
        }

        // Invoke
        Task.Run(async () =>
        {
            TransmissionContext.AsyncLocal.Value = transmissionContext;
            try
            {
                await serviceMethod.Invoke(agentInstance, transmissionContext).ConfigureAwait(false);
                try
                {
                    if (!transmissionContext.IsSent)
                    {
                        transmissionContext.CheckReceiveStream();
                        var result = transmissionContext.Result;
                        if (result == NetResult.Success)
                        {// Success
                            transmissionContext.SendAndForget(transmissionContext.RentMemory, (ulong)result);
                        }
                        else
                        {// Failure
                            transmissionContext.SendAndForget(BytePool.RentMemory.Empty, (ulong)result);
                        }
                    }
                }
                catch
                {
                }
            }
            catch
            {// Unknown exception
                transmissionContext.SendAndForget(BytePool.RentMemory.Empty, (ulong)NetResult.UnknownError);
            }
            finally
            {
                transmissionContext.ReturnAndDisposeStream();
            }
        });
    }

    internal void InvokeSync(TransmissionContext transmissionContext)
    {// transmissionContext.Return();
        if (transmissionContext.DataKind == 0)
        {// Block (Responder)
            if (transmissionContext.DataId == ConnectionAgreement.AuthenticationTokenId)
            {// SetAuthenticationToken
                this.SetAuthenticationToken(transmissionContext);
            }
            else if (transmissionContext.DataId == SetupRelayBlock.DataId)
            {// SetupRelay
                this.NetTerminal.RelayAgent.ProcessSetupRelay(transmissionContext);
            }
            else if (this.NetTerminal.Responders.TryGet(transmissionContext.DataId, out var responder))
            {// Other responders
                responder.Respond(transmissionContext);
            }
            else
            {
                transmissionContext.ReturnAndDisposeStream();
                return;
            }

            /*else if (transmissionContext.DataId == CreateRelayBlock.DataId)
            {
                this.NetTerminal.RelayControl.ProcessCreateRelay(transmissionContext);
            }
            else if (transmissionContext.DataId == ConnectionAgreement.UpdateId)
            {
                this.UpdateAgreement(transmissionContext);
            }
            else if (transmissionContext.DataId == ConnectionAgreement.BidirectionalId)
            {
                this.ConnectBidirectionally(transmissionContext);
            }*/
        }
        else if (transmissionContext.DataKind == 1)
        {// RPC
            Task.Run(() => this.InvokeRPC(transmissionContext));
            return;
        }

        /*if (!this.InvokeCustom(transmissionContext))
        {
            transmissionContext.Return();
        }*/
    }

    internal async Task InvokeRPC(TransmissionContext transmissionContext)
    {
        // Get ServiceMethod
        (var serviceMethod, var agentInstance) = this.TryGetServiceMethod(transmissionContext.DataId);
        if (serviceMethod == null)
        {
            goto SendNoNetService;
        }

        // Invoke
        TransmissionContext.AsyncLocal.Value = transmissionContext;
        try
        {
            await serviceMethod.Invoke(agentInstance, transmissionContext).ConfigureAwait(false);
            try
            {
                if (transmissionContext.ServerConnection.IsClosedOrDisposed)
                {
                }
                else if (!transmissionContext.IsSent)
                {
                    transmissionContext.CheckReceiveStream();
                    var result = transmissionContext.Result;
                    if (result == NetResult.Success)
                    {// Success
                        transmissionContext.SendAndForget(transmissionContext.RentMemory, (ulong)result);
                    }
                    else
                    {// Failure
                        transmissionContext.SendResultAndForget(result);
                    }
                }
            }
            catch
            {
            }
        }
        catch
        {// Unknown exception
            transmissionContext.SendResultAndForget(NetResult.UnknownError);
        }
        finally
        {
            transmissionContext.ReturnAndDisposeStream();
        }

        return;

SendNoNetService:
        transmissionContext.SendAndForget(BytePool.RentMemory.Empty, (ulong)NetResult.NoNetService);
        transmissionContext.ReturnAndDisposeStream();
        return;
    }

    private void SetAuthenticationToken(TransmissionContext transmissionContext)
    {
        if (!TinyhandSerializer.TryDeserialize<AuthenticationToken>(transmissionContext.RentMemory.Memory.Span, out var token))
        {
            transmissionContext.Return();
            return;
        }

        transmissionContext.Return();

        _ = Task.Run(() =>
        {
            var result = NetResult.Success;
            if (this.AuthenticationToken is null)
            {
                if (this.ServerConnection.ValidateAndVerifyWithSalt(token))
                {
                    this.AuthenticationToken = token;
                }
                else
                {
                    result = NetResult.InvalidData;
                }
            }

            transmissionContext.SendAndForget(result, ConnectionAgreement.AuthenticationTokenId);
        });
    }

    /*private void UpdateAgreement(TransmissionContext transmissionContext)
    {
        if (!TinyhandSerializer.TryDeserialize<CertificateToken<ConnectionAgreement>>(transmissionContext.Owner.Memory.Span, out var token))
        {
            transmissionContext.Return();
            return;
        }

        transmissionContext.Return();

        _ = Task.Run(() =>
        {
            var result = this.RespondUpdateAgreement(token);
            if (result)
            {
                this.ServerConnection.Agreement.AcceptAll(token.Target);
                this.ServerConnection.ApplyAgreement();
            }

            transmissionContext.SendAndForget(result, ConnectionAgreement.UpdateId);
        });
    }

    private void ConnectBidirectionally(TransmissionContext transmissionContext)
    {
        TinyhandSerializer.TryDeserialize<CertificateToken<ConnectionAgreement>>(transmissionContext.Owner.Memory.Span, out var token);
        transmissionContext.Return();

        _ = Task.Run(() =>
        {
            var result = this.RespondConnectBidirectionally(token);
            if (result)
            {
                this.ServerConnection.Agreement.EnableBidirectionalConnection = true;
            }

            transmissionContext.SendAndForget(result, ConnectionAgreement.BidirectionalId);
        });
    }*/

    private (ServiceMethod? ServiceMethod, object AgentInstance) TryGetServiceMethod(ulong dataId)
    {
        var serviceId = unchecked((uint)(dataId >> 32));
        if (!this.serviceTable.TryGetAgent(serviceId, out var agent))
        {// No agent (implementation)
            return default;
        }

        var agentInformation = agent.AgentInformation;
        if (!agentInformation.TryGetMethod(dataId, out var serviceMethod))
        {// No method
            return default;
        }

        if (this.agentInstances[agent.Index] is null)
        {
            var instance = this.ServiceProvider?.GetService(agent.AgentInformation.AgentType);
            instance ??= agent.AgentInformation.CreateAgent?.Invoke();
            if (instance is null)
            {// No instance
                return default;
            }

            Interlocked.CompareExchange(ref this.agentInstances[agent.Index], instance, null);
        }

        return (serviceMethod, this.agentInstances[agent.Index]);
    }
}
