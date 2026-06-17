import "dotenv/config";
import { loadConfig } from "./config.js";
import { createServer } from "./server.js";

// Validate env up front and fail fast on bad config before serving any request.
const config = loadConfig();

createServer().listen(config.PORT, () => {
  console.log(`Shopping Agent Orchestrator (AG-UI) listening on http://localhost:${config.PORT}`);
  console.log(`${config.OPENAI_BASE_URL} Using router model: ${config.ROUTER_MODEL} and synthesizer model: ${config.SYNTH_MODEL}` );
});
