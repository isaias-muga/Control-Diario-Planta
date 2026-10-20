import { Rol } from '../utils/jwt';

export interface Usuario {
  id: string;
  username: string;
  nombre: string;
  rol: Rol;
  activo: boolean;
  debeCambiarPassword: boolean;
  /** An owner account (set by configuration only): no other Admin can deactivate it, change its role or reset its password. */
  protegido: boolean;
}

export interface CrearUsuario {
  username: string;
  nombre: string;
  rol: Rol;
  password: string;
}

export interface Operario {
  id: string;
  nombre: string;
  activo: boolean;
}

export interface CrearOperario {
  nombre: string;
  pin: string;
}
