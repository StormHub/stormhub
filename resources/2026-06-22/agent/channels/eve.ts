import { eveChannel } from "eve/channels/eve";
// import { localDev, placeholderAuth, vercelOidc } from "eve/channels/auth";
import { localDev, placeholderAuth } from "eve/channels/auth";

export default eveChannel({
  auth: [
    localDev(),
    // vercelOidc(),
    placeholderAuth(),
  ],
});
