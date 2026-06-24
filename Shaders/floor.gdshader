shader_type spatial;

//uniform vec3 grid_color : source_color = vec3(0.0, 0.2, 0.5);
//uniform vec3 base_color : source_color = vec3(0.02, 0.02, 0.05);

uniform vec3 grid_color : source_color = vec3(0.16, 0.0, 0.25);
uniform vec3 base_color : source_color = vec3(0.1, 0.00, 0.25);
uniform float line_thickness = 4.4;
uniform float speed = 2.0;
uniform float glow = 4.0;

// NEW: Controls how soft the edges of the grid squares are
uniform float edge_blend = 0.19;

// NEW: Control how fast the individual elements spin
uniform float rotation_speed = 4;

float seed() {
    return fract(sin(TIME * 12.9898) * 43758.5453123);
}

float chaos(float min, float max) {
    return floor(seed() * (max - min + 1.0) + min);
}

float chaosFloat(float min, float max) {
    min *= 100.0;
    max *= 100.0;
    return floor((seed() * (max - min + 1.0)) + min) / 100.0;
}

// A standard 2D spatial hash
float seed(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453123);
}

float oscillate_linear(float x, float y, float osc_speed) {
    // mod() and abs() combine to create a sharp "triangle" wave
    float weight = abs(mod(TIME * osc_speed - 1.0, 2.0) - 1.0);
    return mix(x, y, weight);
}

float randomFloat(float min, float max, vec2 uv) {
    min *= 100.0;
    max *= 100.0;
    return floor((seed(uv) * (max - min + 1.0)) + min) / 100.0;
}

void fragment() {
    // Animate the UV over time to make the grid flow
    vec2 uv = UV * oscillate_linear(10.0, 200.0, 0.03);

    uv.y += TIME * speed;
    uv.x += TIME * speed;

    // 1. Get a unique ID for the current grid cell
    vec2 cell_id = floor(uv);
    vec2 cell_uv = fract(uv);

    // 2. Get the local coordinates inside the current cell (0.0 to 1.0)
    vec2 local_uv = fract(uv);

    // 3. Set up the rotation angle
    // We use the cell_id to give each cell a slightly randomized rotation speed and direction!
    // (Remove the randomFloat multiplication if you want them all to rotate in perfect sync)
//     float angle = TIME * rotation_speed * randomFloat(-1.0, 1.0, cell_id);

    // 3. Set up the rotation angle
    // Get a base speed that never hits zero (between 0.4x and 1.2x your rotation_speed)
    float cell_speed = randomFloat(0.4, 1.2, cell_id);

    // Randomly pick a direction (1.0 for clockwise, -1.0 for counter-clockwise)
    // We offset the cell_id slightly so the direction gets a uniquely different random roll than the speed
    float cell_direction = randomFloat(0.0, 1.0, cell_id + vec2(12.3, 45.6)) > 0.5 ? 1.0 : -1.0;

    // Calculate final angle
    float angle = TIME * rotation_speed * cell_speed * cell_direction;

    // 4. Create the 2D rotation matrix
    mat2 rot = mat2(
        vec2(cos(angle), sin(angle)),
        vec2(-sin(angle), cos(angle))
    );

    // 5. Shift to center, rotate, and shift back
    local_uv -= 0.5;
    local_uv = rot * local_uv;
    local_uv += 0.5;

    // Calculate grid lines using the NEW rotated local_uv
    vec2 dist = abs(local_uv - 0.5);

    // Smooth the lines for better antialiasing
    float line_x = smoothstep(0.5 - line_thickness, 0.5, dist.x);
    float line_y = smoothstep(0.5 - line_thickness, 0.5, dist.y);

    // --- NEW: EDGE BLEND MASK ---
    // Calculate distance from the center of the UNROTATED cell
    vec2 fade_dist = abs(cell_uv - 0.5);

    // Smoothly fade to 0.0 as we approach the very edges (0.5) of the cell boundary
    float fade_x = smoothstep(0.5, 0.5 - edge_blend, fade_dist.x);
    float fade_y = smoothstep(0.5, 0.5 - edge_blend, fade_dist.y);
    float edge_mask = fade_x * fade_y;
    // ----------------------------

    // Combine x and y lines (changed to just X to make it striped instead of a grid)
    // CRITICAL FIX: Pass 'cell_id' instead of 'uv' so the whole cell shares one random brightness
    float grid_mask = line_x * randomFloat(0.9, 1.5, cell_id) * randomFloat(0.2, 0.8, cell_id);

    // CRITICAL: Multiply the final shape by the edge mask to hide the seams
    grid_mask *= edge_mask;

    // Add a cool pulsing wave effect across the floor
    float pulse = (sin(TIME * 2.0 + UV.x * 5.0 + UV.y * 5.0) + 1.0) * 0.5;

    // Set material properties
    ALBEDO = mix(base_color, grid_color, grid_mask);
    EMISSION = grid_color * grid_mask * pulse * glow;
    METALLIC = 0.5;
    ROUGHNESS = 0.3;
}