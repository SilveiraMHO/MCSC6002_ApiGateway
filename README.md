# MCSC6002_ApiGateway
API Gateway built with ASP.NET Core and Ocelot for microservices routing and management.

## mTLS certificates (gateway to services)
The services accept HTTPS connections only from the gateway's client certificate, issued by a project CA. Certificates are never committed. Generate them once in `keys/tls/` next to the service repos (Git Bash):

```bash
mkdir -p keys/tls && cd keys/tls
export MSYS_NO_PATHCONV=1
openssl req -x509 -new -newkey rsa:2048 -nodes -keyout ca.key -out ca.crt -days 825 -subj "/CN=MCSC6002 Dev CA" -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign"
openssl req -new -newkey rsa:2048 -nodes -keyout service.key -out service.csr -subj "/CN=localhost"
printf "basicConstraints=CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectAltName=DNS:localhost\n" > service.ext
openssl x509 -req -in service.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out service.crt -days 825 -extfile service.ext
openssl req -new -newkey rsa:2048 -nodes -keyout gateway-client.key -out gateway-client.csr -subj "/CN=api-gateway"
printf "basicConstraints=CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=clientAuth\n" > client.ext
openssl x509 -req -in gateway-client.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out gateway-client.crt -days 825 -extfile client.ext
openssl pkcs12 -export -inkey service.key -in service.crt -out service.pfx -passout pass:
openssl pkcs12 -export -inkey gateway-client.key -in gateway-client.crt -out gateway-client.pfx -passout pass:
rm -f *.csr *.ext ca.srl
```

The gateway gets `gateway-client.pfx` and `ca.crt`; each service gets `service.pfx` and `ca.crt`.
