// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Responder;

namespace Netsphere.Relay;

public class CertificateRelayControl : IRelayControl
{
    public static readonly IRelayControl Instance = new CertificateRelayControl();

    private class CreateRelayResponder : AsyncResponder<CertificateToken<AssignRelayBlock>, AssignRelayResponse>
    {
        public CreateRelayResponder(CertificateRelayControl relayControl)
        {
            this.relayControl = relayControl;
        }

        public override NetResultValue<AssignRelayResponse> RespondAsync(CertificateToken<AssignRelayBlock> token)
        {
            if (this.ServerConnection.NetTerminal.RelayControl is not CertificateRelayControl ||
                !token.PublicKey.Equals(this.relayControl.CertificatePublicKey) ||
                !token.ValidateAndVerify(this.ServerConnection))
            {
                return new(NetResult.NotAuthenticated);
            }

            var relayAgent = this.ServerConnection.NetTerminal.RelayAgent;
            var result = relayAgent.AddExchange(this.ServerConnection, token.Target, out var innerRelayId, out var outerRelayId);
            var relayPoint = this.relayControl.DefaultMaxRelayPoint;
            var retensionMics = this.relayControl.DefaultRelayRetensionMics;
            var response = new AssignRelayResponse(result, innerRelayId, outerRelayId, relayPoint, retensionMics, this.ServerConnection.NetTerminal.NetStats.OwnNetNode);
            this.ServerConnection.Agreement.MinimumConnectionRetentionMics = retensionMics;
            relayAgent.AddRelayPoint(innerRelayId, relayPoint);

            return new(NetResult.Success, response);
        }

        private readonly CertificateRelayControl relayControl;
    }

    public int MaxRelayExchanges
        => 100;

    public long DefaultRelayRetensionMics
        => Mics.FromMinutes(5);

    public long DefaultMaxRelayPoint
        => 100_000;

    public long DefaultRestrictedIntervalMics
        => 20_000;

    public SignaturePublicKey CertificatePublicKey { get; private set; }

    public void RegisterResponder(ResponderControl responders)
    {
        responders.Register(new CreateRelayResponder(this));
    }

    public void SetCertificatePublicKey(SignaturePublicKey publicKey)
    {
        this.CertificatePublicKey = publicKey;
    }
}
