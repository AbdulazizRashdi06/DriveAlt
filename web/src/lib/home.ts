import type { AppConfig, Me } from './api'

/** Where someone lands after signing in or continuing as a guest: chat, unless they're a guest who can't chat. */
export function homePath(me: Me | null, config: AppConfig) {
  return me?.role === 'guest' && !config.guestChat ? '/browse' : '/chat'
}
