const e = encodeURIComponent;
export const paths = {
  explorer: (view?: 'cards' | 'connections' | 'graph') => (view && view !== 'cards' ? `/?view=${view}` : '/'),
  team: (id: string) => `/?team=${e(id)}`,
  teamRecord: (id: string) => `/teams/${e(id)}`,
  proposal: (id: string) => `/review/${e(id)}`,
  appLens: (id: string) => `/applications/${e(id)}/lens`,
  flowLens: (id: string) => `/flows/${e(id)}/lens`,
  lineage: (id: string) => `/data-objects/${e(id)}`,
  app: (id: string) => `/applications/${e(id)}`,
  flow: (id: string) => `/flows/${e(id)}`,
};

/** Lets the graph view take over "pick an application" from the search box while it is shown. */
export const searchBridge: { pickApp: ((id: string) => void) | null } = { pickApp: null };
