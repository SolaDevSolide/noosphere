import type { Landscape, LandscapeApplication, LandscapeFlow } from '../api/types';
import { buildModel } from '../domain/model';
import { applications, dataObjects, domains, flows, teams } from '../mocks/alize';

/** The demo landscape as a member sees it: the restricted application and its flows are reduced. */
export function memberLandscape(): Landscape {
  const restricted = new Set(applications.filter((a) => a.visibility === 'restricted').map((a) => a.id));
  const apps: LandscapeApplication[] = applications.map((a) =>
    restricted.has(a.id) ? { id: a.id, domainId: a.domainId, restricted: true } : a,
  );
  const fl: LandscapeFlow[] = flows.map((f) =>
    restricted.has(f.fromId) || restricted.has(f.toId)
      ? { id: f.id, fromId: f.fromId, toId: f.toId, type: f.type, restricted: true }
      : f,
  );
  return {
    domains,
    teams,
    applications: apps,
    dataObjects,
    flows: fl,
    stats: { applications: apps.length, flows: fl.length, domains: domains.length },
    generatedAt: '2026-10-30T00:00:00Z',
  };
}
export const memberModel = () => buildModel(memberLandscape());
