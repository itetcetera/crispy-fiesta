// The controls: the keyboard or a gamepad, as an Atari joystick (one button, 4 directions).
// Stick bits as the Atari reads them: a clear bit is pushed (1 up, 2 down, 4 left, 8 right).

const UP = 1, DOWN = 2, LEFT = 4, RIGHT = 8;

const keys = new Set<string>();

export function stickFrom(up: boolean, down: boolean, left: boolean, right: boolean): number {
  let s = 15;
  if (up && !down) s &= ~UP;
  if (down && !up) s &= ~DOWN;
  if (left && !right) s &= ~LEFT;
  if (right && !left) s &= ~RIGHT;
  return s;
}

function read(): { button: boolean; stick: number } {
  let up = keys.has("ArrowUp") || keys.has("KeyW");
  let down = keys.has("ArrowDown") || keys.has("KeyS");
  let left = keys.has("ArrowLeft") || keys.has("KeyA");
  let right = keys.has("ArrowRight") || keys.has("KeyD");
  let button = keys.has("Space") || keys.has("Enter");
  for (const pad of navigator.getGamepads?.() ?? []) {
    if (!pad) continue;
    const [x = 0, y = 0] = pad.axes;
    const b = (i: number) => pad.buttons[i]?.pressed ?? false;
    up ||= y < -0.5 || b(12);
    down ||= y > 0.5 || b(13);
    left ||= x < -0.5 || b(14);
    right ||= x > 0.5 || b(15);
    button ||= b(0) || b(1);
  }
  return { button, stick: stickFrom(up, down, left, right) };
}

/** Sends the controls whenever they change (polled each animation frame for the gamepad). */
export function watchControls(send: (button: boolean, stick: number) => void, active: () => boolean) {
  const game = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Space", "Enter", "KeyW", "KeyA", "KeyS", "KeyD"]);
  window.addEventListener("keydown", (e) => {
    if (!active() || !game.has(e.code)) return;
    keys.add(e.code);
    e.preventDefault();
  });
  window.addEventListener("keyup", (e) => keys.delete(e.code));
  window.addEventListener("blur", () => keys.clear());
  let last = "";
  const poll = () => {
    if (active()) {
      const now = read();
      const k = `${now.button}/${now.stick}`;
      if (k !== last) {
        last = k;
        send(now.button, now.stick);
      }
    }
    requestAnimationFrame(poll);
  };
  requestAnimationFrame(poll);
}
