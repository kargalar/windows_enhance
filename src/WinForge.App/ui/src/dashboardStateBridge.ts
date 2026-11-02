type CatalogState = { modules: { id: string }[] };
type Change = { path: string[]; value?: unknown; remove?: boolean };
type ModuleDelta = { id: string; changes: Change[] };
type BridgeResult<T> = { state: T | null; accepted: boolean; resync: boolean; error?: string; requestId?: string };

function changeAt(node: unknown, change: Change, depth = 0): unknown {
  if (depth === change.path.length) {
    if (change.remove) throw new Error('Cannot remove a module root');
    return change.value;
  }
  if (!node || typeof node !== 'object' || depth >= 64) throw new Error('Invalid state change path');
  const key = change.path[depth];
  const leaf = depth === change.path.length - 1;
  if (Array.isArray(node)) {
    if (!/^(0|[1-9]\d*)$/.test(key)) throw new Error('Invalid array index');
    const index = Number(key);
    if (!Number.isSafeInteger(index) || index >= node.length || leaf && change.remove) throw new Error('Invalid array change');
    const copy = node.slice();
    copy[index] = leaf ? change.value : changeAt(node[index], change, depth + 1);
    return copy;
  }
  const record = node as Record<string, unknown>;
  if ((!leaf || change.remove) && !Object.prototype.hasOwnProperty.call(record, key)) throw new Error('Missing state change parent');
  const copy = { ...record };
  if (leaf && change.remove) delete copy[key];
  else Object.defineProperty(copy, key, { value: leaf ? change.value : changeAt(record[key], change, depth + 1), enumerable: true, writable: true, configurable: true });
  return copy;
}

// Full snapshots establish a session/version. Patches must continue it exactly;
// a gap requests one fresh snapshot rather than guessing at missing state.
export class DashboardStateBridge<T extends CatalogState> {
  private state: T | null = null;
  private session: string | undefined;
  private version = 0;
  private resyncPending = false;
  private retiredSessions = new Set<string>();

  accept(input: unknown): BridgeResult<T> {
    const ignored = (): BridgeResult<T> => ({ state: this.state, accepted: false, resync: false });
    const resync = (): BridgeResult<T> => {
      const request = !this.resyncPending;
      this.resyncPending = true;
      return { state: this.state, accepted: false, resync: request };
    };
    if (!input || typeof input !== 'object') return ignored();
    const message = input as Record<string, unknown>;
    if (!['state', 'state.patch', 'error', 'protocol.error'].includes(String(message.type))) return ignored();
    const error = typeof message.message === 'string' ? message.message : undefined;
    const requestId = typeof message.requestId === 'string' ? message.requestId : undefined;
    if (message.type === 'protocol.error') return { ...ignored(), error, requestId };
    const versioned = message.protocolVersion === 1 && typeof message.sessionId === 'string'
      && Number.isSafeInteger(message.version) && Number(message.version) > 0;
    if (message.protocolVersion !== undefined && !versioned) return resync();
    if (versioned && this.retiredSessions.has(String(message.sessionId))) return ignored();
    if (this.session && !versioned) return ignored();
    if (versioned && this.session === message.sessionId && Number(message.version) <= this.version) return ignored();
    if (!message.payload || typeof message.payload !== 'object') return resync();
    const payload = message.payload as Partial<T> & { moduleChanges?: ModuleDelta[] };
    const modules = payload.modules;
    const validModules = Array.isArray(modules) && modules.every(module => module && typeof module.id === 'string')
      && new Set(modules.map(module => module.id)).size === modules.length;

    if (message.type === 'state.patch') {
      if (this.resyncPending) return ignored();
      if (!versioned || !this.state || this.session !== message.sessionId || message.baseVersion !== this.version
          || Number(message.version) !== this.version + 1) return resync();
      if (modules !== undefined && !validModules) return resync();
      const existing = new Set(this.state.modules.map(module => module.id));
      if (modules?.some(module => !existing.has(module.id))) return resync();
      const replacements = new Map(modules?.map(module => [module.id, module]));
      const deltas = payload.moduleChanges;
      if (deltas !== undefined && !Array.isArray(deltas)) return resync();
      const touched = new Set(replacements.keys());
      try {
        for (const delta of deltas ?? []) {
          if (!delta || typeof delta.id !== 'string' || !existing.has(delta.id) || touched.has(delta.id)
              || !Array.isArray(delta.changes) || delta.changes.length > 4096) return resync();
          touched.add(delta.id);
          let module: unknown = this.state.modules.find(module => module.id === delta.id)!;
          for (const change of delta.changes) {
            if (!change || !Array.isArray(change.path) || !change.path.every(part => typeof part === 'string')
                || change.remove !== undefined && typeof change.remove !== 'boolean'
                || !change.remove && !Object.prototype.hasOwnProperty.call(change, 'value')) return resync();
            module = changeAt(module, change);
          }
          if (!module || typeof module !== 'object' || (module as { id: string }).id !== delta.id) return resync();
          if (delta.changes.length) replacements.set(delta.id, module as T['modules'][number]);
        }
      } catch { return resync(); }
      const merged = replacements.size ? this.state.modules.map(module => replacements.get(module.id) ?? module) : this.state.modules;
      const { moduleChanges: _changes, modules: _modules, ...metadata } = payload;
      this.state = { ...this.state, ...metadata, modules: merged } as T;
    } else {
      if (!validModules) return resync();
      if (versioned && this.session && this.session !== message.sessionId) {
        this.retiredSessions.add(this.session);
        if (this.retiredSessions.size > 16) this.retiredSessions.delete(this.retiredSessions.values().next().value!);
      }
      // Legacy full snapshots are accepted only before versioned negotiation.
      this.state = { ...payload, ...(message.persistence ? { persistence: message.persistence } : {}) } as T;
      this.resyncPending = false;
    }
    if (versioned) { this.session = String(message.sessionId); this.version = Number(message.version); }
    return { state: this.state, accepted: true, resync: false, error: message.type === 'error' ? error : undefined, requestId };
  }
}
