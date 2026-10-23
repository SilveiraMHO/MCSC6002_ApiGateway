using Ocelot.Configuration;
using Ocelot.Logging;
using Ocelot.Requester;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ApiGateway
{
    // mTLS towards the downstream services. Ocelot has no client-certificate setting, so the HTTP handler
    // it builds per route is extended here: the gateway presents its client certificate and accepts only
    // server certificates issued by the project CA.
    public class MtlsMessageInvokerPool : MessageInvokerPool
    {
        private readonly X509Certificate2 _clientCertificate;
        private readonly X509Certificate2 _ca;

        public MtlsMessageInvokerPool(IDelegatingHandlerFactory handlerFactory, IOcelotLoggerFactory loggerFactory, IConfiguration configuration)
            : base(handlerFactory, loggerFactory)
        {
            _clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(configuration["MutualTls:ClientCertificatePath"]!, password: null);
            _ca = X509CertificateLoader.LoadCertificateFromFile(configuration["MutualTls:CaCertificatePath"]!);
        }

        protected override SocketsHttpHandler CreateHandler(DownstreamRoute downstreamRoute)
        {
            var handler = base.CreateHandler(downstreamRoute);

            handler.SslOptions.ClientCertificates = new X509CertificateCollection { _clientCertificate };
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                certificate is X509Certificate2 serverCertificate
                && !errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                && IsIssuedByCa(serverCertificate);

            return handler;
        }

        private bool IsIssuedByCa(X509Certificate2 certificate)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust; // Trust only the project CA, not the OS store.
            chain.ChainPolicy.CustomTrustStore.Add(_ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Limitation: dev CA has no CRL, so issued certs cannot be revoked.
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // EKU: server authentication.

            return chain.Build(certificate);
        }
    }
}
