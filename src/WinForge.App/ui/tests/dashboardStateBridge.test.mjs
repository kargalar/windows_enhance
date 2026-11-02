import assert from 'node:assert/strict';
import test from 'node:test';
import { DashboardStateBridge } from '../src/dashboardStateBridge.ts';

const initial = () => ({ modules: [{ id: 'a', isActive: false, settings: [{ key: 'value', value: 1 }] },
  { id: 'b', isActive: true, settings: [{ key: 'value', value: 2 }] }], activeCount: 1, appearance: { tone: 'Açık' } });
const snapshot = (version, payload = initial()) => ({ type: 'state', protocolVersion: 1, sessionId: 'native', version, payload });
const patch = (version, baseVersion, payload) => ({ type: 'state.patch', protocolVersion: 1, sessionId: 'native', version, baseVersion, payload });

test('module patches preserve unrelated modules and reuse their object references', () => {
  const bridge = new DashboardStateBridge();
  const before = bridge.accept(snapshot(1)).state;
  const replacement = { ...before.modules[0], isActive: true, settings: [{ key: 'value', value: 3 }] };
  const result = bridge.accept({ ...patch(2, 1, { modules: [replacement], activeCount: 2 }), requestId: 'edit-1' });
  assert.equal(result.accepted, true);
  assert.equal(result.requestId, 'edit-1');
  assert.equal(result.state.modules[0], replacement);
  assert.equal(result.state.modules[1], before.modules[1]);
  assert.equal(result.state.appearance, before.appearance);
  assert.equal(result.state.activeCount, 2);
});

test('stale replies and stale errors cannot overwrite newer accepted edits', () => {
  const bridge = new DashboardStateBridge();
  bridge.accept(snapshot(1));
  const newer = initial();
  newer.modules[0].settings[0].value = 30;
  const latest = bridge.accept(snapshot(3, newer)).state;
  assert.equal(bridge.accept(patch(2, 1, { modules: [{ id: 'a', isActive: false }] })).accepted, false);
  const error = bridge.accept({ ...snapshot(2), type: 'error', message: 'obsolete failure' });
  assert.equal(error.accepted, false);
  assert.equal(error.error, undefined);
  assert.equal(error.state, latest);
});

test('a lost patch requests one full resync and later patches wait for the snapshot', () => {
  const bridge = new DashboardStateBridge();
  const before = bridge.accept(snapshot(1)).state;
  assert.equal(bridge.accept(patch(3, 2, { modules: [] })).resync, true);
  assert.equal(bridge.accept(patch(4, 3, { modules: [] })).resync, false);
  assert.equal(bridge.accept(patch(5, 1, { modules: [] })).state, before);
  assert.equal(bridge.accept(snapshot(6)).accepted, true);
  assert.equal(bridge.accept(patch(7, 6, { modules: [], persistence: { pending: false } })).accepted, true);
});

test('a fresh page or changed native session needs a full snapshot before patches', () => {
  const bridge = new DashboardStateBridge();
  assert.equal(bridge.accept(patch(1, 0, { modules: [] })).resync, true);
  bridge.accept(snapshot(2));
  assert.equal(bridge.accept({ ...patch(3, 2, { modules: [] }), sessionId: 'reconnected' }).resync, true);
  assert.equal(bridge.accept({ ...snapshot(1), sessionId: 'reconnected' }).accepted, true);
  assert.equal(bridge.accept({ ...patch(2, 1, { modules: [] }), sessionId: 'reconnected' }).accepted, true);
  assert.equal(bridge.accept(snapshot(99)).accepted, false);
});

test('metadata-only patches retain the catalog; unexpected catalog members trigger resync', () => {
  const bridge = new DashboardStateBridge();
  const before = bridge.accept(snapshot(1)).state;
  const result = bridge.accept(patch(2, 1, { modules: [], persistence: { pending: false, savedVersion: 10 } }));
  assert.equal(result.state.modules, before.modules);
  assert.equal(result.state.persistence.savedVersion, 10);
  assert.equal(bridge.accept(patch(3, 2, { modules: [{ id: 'unknown' }] })).resync, true);
});

test('malformed patches and unversioned replies cannot bypass negotiated ordering', () => {
  const bridge = new DashboardStateBridge();
  bridge.accept(snapshot(1));
  assert.equal(bridge.accept({ type: 'state', payload: initial() }).accepted, false);
  assert.equal(bridge.accept(patch(2, 1, { modules: [{ id: 'a' }, { id: 'a' }] })).resync, true);
});

test('field changes update a catalog entry without copying its image or mutating prior state', () => {
  const bridge = new DashboardStateBridge();
  const value = initial();
  value.modules[0].panel = { entries: [{ id: 'one', visible: true, image: 'large-image' }, { id: 'two', visible: true }] };
  const before = bridge.accept(snapshot(1, value)).state;
  const result = bridge.accept(patch(2, 1, { modules: [], moduleChanges: [{ id: 'a', changes: [
    { path: ['panel', 'entries', '0', 'visible'], value: false, remove: false },
    { path: ['settings', '0', 'value'], value: 40, remove: false },
  ] }] }));
  assert.equal(result.accepted, true);
  assert.equal(result.state.modules[0].panel.entries[0].visible, false);
  assert.equal(result.state.modules[0].panel.entries[0].image, 'large-image');
  assert.equal(result.state.modules[0].settings[0].value, 40);
  assert.equal(result.state.modules[0].panel.entries[1], before.modules[0].panel.entries[1]);
  assert.equal(result.state.modules[1], before.modules[1]);
  assert.equal(before.modules[0].panel.entries[0].visible, true);
  assert.equal('moduleChanges' in result.state, false);
});

test('invalid field changes reject the entire update and array replacement/removal remain explicit', () => {
  const bridge = new DashboardStateBridge();
  const before = bridge.accept(snapshot(1)).state;
  const failed = bridge.accept(patch(2, 1, { moduleChanges: [{ id: 'a', changes: [
    { path: ['isActive'], value: true }, { path: ['missing', 'parent'], value: 1 },
  ] }] }));
  assert.equal(failed.resync, true);
  assert.equal(failed.state, before);
  assert.equal(before.modules[0].isActive, false);
  bridge.accept(snapshot(3));
  const updated = bridge.accept(patch(4, 3, { moduleChanges: [{ id: 'a', changes: [
    { path: ['settings'], value: [] }, { path: ['isActive'], remove: true },
  ] }] }));
  assert.deepEqual(updated.state.modules[0].settings, []);
  assert.equal('isActive' in updated.state.modules[0], false);
});

test('JSON object keys cannot alter prototypes and a skipped sequence requires resync', () => {
  const bridge = new DashboardStateBridge();
  bridge.accept(snapshot(1));
  const result = bridge.accept(patch(2, 1, { moduleChanges: [{ id: 'a', changes: [
    { path: ['__proto__'], value: { polluted: true } },
  ] }] }));
  assert.equal(result.accepted, true);
  assert.equal({}.polluted, undefined);
  assert.equal(Object.getPrototypeOf(result.state.modules[0]), Object.prototype);
  assert.equal(Object.prototype.hasOwnProperty.call(result.state.modules[0], '__proto__'), true);
  assert.equal(bridge.accept(patch(4, 2, { modules: [] })).resync, true);
});
