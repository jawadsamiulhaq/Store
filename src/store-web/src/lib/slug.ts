/**
 * Client-side slug preview.
 *
 * The server is the authority: `SlugGenerator` normalises the slug again on write and resolves
 * collisions with a readable `-2` suffix, so what this returns is a *preview* and may differ from
 * what comes back. It exists so the admin can see the URL a name will produce before saving, and
 * so the slug field can be left blank for the common case.
 *
 * Deliberately matches the server's shape: lowercase, ASCII, single hyphens, no leading or
 * trailing separator. The legacy store emitted `ready---canned-food-639` from naive punctuation
 * replacement, which is exactly what the collapse-and-trim steps below prevent.
 */
export function slugify(value: string): string {
  return value
    .normalize('NFKD')
    // Strip combining marks so "Jalapeño" becomes "jalapeno" rather than losing the character.
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}
