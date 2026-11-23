/** Formas de contracts/openapi.yaml (`SessionResponse`, `UserDto`, `TenantDto`). */
export interface UserDto {
  id: string;
  name: string;
  email: string;
}

export interface TenantDto {
  id: string;
  name: string;
  role: 'owner';
}

export interface SessionResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: UserDto;
  tenant: TenantDto;
}
