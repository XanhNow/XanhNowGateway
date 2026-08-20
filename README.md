# XanhNow Gateway

Public edge/BFF for XanhNow mobile and future clients.

Responsibilities:

- Own the public `api.ioxy.site` edge contract.
- Serve platform identity metadata such as `/.well-known/assetlinks.json`.
- Route public traffic to independent child apps such as Security, Customer and Object Storage.
- Keep child-app domain logic outside the gateway.

Local routes:

- `/security/{**path}` -> XanhNow.Security.Api
- `/customer/{**path}` -> XanhNow.Customer.Api
- `/object-storage/{**path}` -> XanhNow ObjectStorage Api
- `/.well-known/assetlinks.json` -> Android Digital Asset Links
- `/health/live`, `/health/ready` -> gateway health
