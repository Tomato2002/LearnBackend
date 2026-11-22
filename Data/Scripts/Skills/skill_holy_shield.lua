-- Tuyệt chiêu của Arthur: Đánh gây sát thương vừa phải và tự hồi 18% máu tối đa
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()

    local raw_power = math.floor(caster.atk * 1.15)
    ctx:deal_damage(target, raw_power, "Khiên Thánh Đập")

    local heal_amount = math.floor(caster.max_hp * 0.18)
    ctx:heal(caster, heal_amount, "Ánh Sáng Hồi Phục")
end
