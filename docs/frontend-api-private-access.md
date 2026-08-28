# Frontend integration: database-backed Private Access

## Database-mode login

```http
POST /api/v1/private-access/login
Content-Type: application/json
```

```json
{
  "username": "...",
  "password": "...",
  "keyword": "..."
}
```

Always use `credentials: "include"`. A successful database-mode request returns `200 OK`, sets the
HTTP-only private cookie, and returns the same DTO as successful Beeexy email/Google authentication:

```ts
interface AuthenticationTokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  account: {
    accountId: string;
    profileId: string;
    beeexyId: string;
  };
}
```

Hydrate the existing Beeexy authentication store directly, then load `/api/v1/auth/me` and
`/api/v1/patients/me`. Do not add demo-tester flags or special patient selection.

All invalid credentials and unavailable/disabled identities return the generic private-access
`401`. `400` remains reserved for malformed input and `429` includes `Retry-After`.

## Session lifecycle

- `GET /api/v1/private-access/session` returns `{ authenticated, expiresAt }` and clears invalid cookies.
- Continue normal access-token use and refresh rotation while the private cookie remains active.
- Use `POST /api/v1/private-access/logout` for tester logout. It clears the cookie and revokes the
  linked normal refresh family.
- If the normal token state is lost while the private cookie remains, call private logout and show
  the credential form again. Database mode does not silently reissue tokens.
- A gate `401` on any API request returns the tester to the Private Access screen.

## Migration compatibility

During rollout the frontend must handle both successful shapes:

- legacy mode: `204`, followed by the existing bodyless `POST /private-access/guest-session`;
- database mode: `200 AuthenticationTokenResponse`, with no `guest-session` call.

After production switches to database mode, remove the legacy branch. All fetches remain
credentialed for the private cookie, and authenticated product calls also send the normal Bearer token.

