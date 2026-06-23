/** Live supervision state from the running Guardi agent (127.0.0.1:38473). */

export const AGENT_SHIELD_STATE_URL = "http://127.0.0.1:38473/shield-state";

const OFFLINE_STATE = Object.freeze({
  agentRunning: false,
  active: false,
  managed: { shieldActive: false },
});

function readBool(value, fallback = false) {
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const s = value.trim().toLowerCase();
    if (s === "1" || s === "true" || s === "yes" || s === "on") return true;
    if (s === "0" || s === "false" || s === "no" || s === "off") return false;
  }
  return fallback;
}

export async function fetchAgentShieldState() {
  try {
    const res = await fetch(AGENT_SHIELD_STATE_URL, {
      method: "GET",
      cache: "no-store",
    });
    if (!res.ok) return { ...OFFLINE_STATE };
    const data = await res.json();
    if (!data || typeof data !== "object") return { ...OFFLINE_STATE };

    const active = data.agentRunning !== false && data.active === true;
    const sourceManaged = data.managed && typeof data.managed === "object" ? data.managed : {};
    const managedWantsShield =
      "shieldActive" in sourceManaged || "enabled" in sourceManaged
        ? readBool(sourceManaged.shieldActive ?? sourceManaged.enabled, active)
        : active;

    return {
      agentRunning: data.agentRunning !== false,
      active,
      managed: {
        ...sourceManaged,
        shieldActive: active && managedWantsShield,
      },
    };
  } catch {
    return { ...OFFLINE_STATE };
  }
}

/** True only while Guardi is running and filtering is active. */
export async function isAgentSupervisionActive() {
  const agent = await fetchAgentShieldState();
  return agent.agentRunning === true && agent.active === true;
}
