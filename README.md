# Usage

1. For easy wildcard certificate generation, every domain must point to a single subdomain in Route53 zone.
2. For example, CNAME `_acme-challenge` of `domain01.com` must be set to `domain01.com.le01.[wildcardplaceholder.com]`. Suffix must be set to `le01.[wildcardplaceholder.com]`. `[wildcardplaceholder.com]` can contain subdomain.
3. Otherwise, single certificate is generated for requested host name.

# Scope

1. Generate certificates on the fly via http-01 challenges, save in given PG database. Only if the host points to given list of IPs in DNS, we will not try to create ACME certificate if DNS doesn't point.

# Environment Variables

```
AWS_ACCESS_KEY_ID= your aws access key
AWS_SECRET_ACCESS_KEY= secret
AWS_ZONE_ID=
AWS_ZONE_SUFFIX=le01.[wildcardplaceholder.com]

ACME_END_POINT=(production|staging) or full url, default is staging
ACME_EAB=external account binding
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
