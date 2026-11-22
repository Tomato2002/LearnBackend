-- Kỹ năng cơ bản của Lyra: Bắn tia nước gây 115% ATK
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()

    local raw_power = math.floor(caster.atk * 1.15)
    ctx:deal_damage(target, raw_power, "Tia Nước Thánh")
end
