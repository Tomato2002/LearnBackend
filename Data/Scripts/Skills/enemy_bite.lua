-- Đòn đánh cơ bản của quái vật: 105% ATK (có 20% tỉ lệ cắn hiểm 130%)
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()

    local roll = ctx:random_int(1, 100)
    local multiplier = (roll <= 20) and 1.30 or 1.05
    local label = (roll <= 20) and "Cắn Xé (Chí Mạng)" or "Cắn Xé"

    local raw_power = math.floor(caster.atk * multiplier)
    ctx:deal_damage(target, raw_power, label)
end
