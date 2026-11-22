-- Quả Cầu Lửa (Ignis / Boss Rồng Lửa): 145% ATK + 30% tỉ lệ bạo kích bằng RNG tất định từ Server
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()

    local roll = ctx:random_int(1, 100)
    local is_crit = roll <= 30
    local multiplier = is_crit and 1.95 or 1.45
    local label = is_crit and "Quả Cầu Lửa (BẠO KÍCH!)" or "Quả Cầu Lửa"

    local raw_power = math.floor(caster.atk * multiplier)
    ctx:deal_damage(target, raw_power, label)
end
