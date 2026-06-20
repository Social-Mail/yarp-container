# Features
1. YARP Reverse Proxy
2. Automatic certificate installation with following conditions.
    * If host points to `SELF_IPs` environment variable, then single host certificate is installed.
    * If CNAME `_acme-challenge` prefix points to provided `AWS_ZONE_SUFFIX` environment variable. For exmaple, `_acme-challenge.zone01.com` points to `zone01.com.le01.[WildCardPlaceHolder.com]` where `le01.[WildCardPlaceHolder.com]` is zone suffix.
3. Even for `WildCardPlaceHolder.com`, same configuration works, so you just need to supply single credential set. To support wildcard for multiple domains.
4. Applys rate limiting by default.
5. Setup host mapping via `forward.json`, format specified below.
6. Wildcard mapping in `forward.json` will forward request to HTTP server and use text retrieved to forward further. This is in case if a server is multitanent server and it needs to map host to different port.

# Environment Variables

```
AWS_ACCESS_KEY_ID= your aws access key
AWS_SECRET_ACCESS_KEY= secret
AWS_ZONE_ID=
AWS_ZONE_SUFFIX=le01.[wildcardplaceholder.com]

ACME_END_POINT=(production|staging) or full url, default is staging
ACME_EAB_KID=external account binding
ACME_EAB_HMAC=hmac

# This will be used to check if given host points to this IP or not
# Only if it matches the IP, ACME certificate will be requested
SELF_IPs=

FORWARD_CERT_STORE=/cache/certs/ <-- local store

# If set, it will be used to query host to port mapping
FORWARD_JSON=/app/forward.json
# if set, it will use this when host isn't specified in forward.json
FORWARD_HOST=0.0.0.0
FORWARD_PORT= # can be unix path
```

# forward.json

```json
{
    "host:5001": [ "host1.com", "sub-domain.host1.com", "*.xyz.com" ],

    // this will use 0.0.0.0 connector or FORWARD_HOST
    "5002": [ "host2.com" ],
    "/sockets/unix-path.sock": ["host2.com", "host4.com"],

    // This must be last entry...
    // This is not the forward address for all hosts
    // yarp-container will first query host name on this port
    // and use given forward location to route further
    "8001": "*"
}
```

In above example, port 8001 is special port which runs a HTTP server and which will return the address where yarp should connect further. For example, if you have a multitanent cluster running on port 8001 and every host is running on different dynamically generated port. This tanent server will start the web application on demand.
