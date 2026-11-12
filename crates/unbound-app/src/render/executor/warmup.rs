use super::*;

impl UnityRenderer {
    /// Prepare real GPU programs and pipelines for the current attachment configuration.
    /// As with an ordinary draw, a different later render state can require another pipeline.
    pub fn shader_warmup(
        &mut self,
        request: unbound_runtime::render_protocol::ShaderWarmupRequest,
    ) -> Result<String, String> {
        if request.stats {
            let (programs, pipelines) = self.compiled.prepared_counts(&request.shader);
            return Ok(
                serde_json::json!({"programs": programs, "pipelines": pipelines}).to_string(),
            );
        }
        let material = owned::Material {
            shader: request.shader,
            render_queue: -1,
            keywords: request.keywords,
            ..Default::default()
        };
        let sources = if request.all_variants {
            self.resolver.warmup_sources(&material)?
        } else {
            vec![self.resolver.resolve(&material)]
        };
        let target = match self.state.target.clone() {
            Some(target) => target,
            None => self.screen_target(self.color_space),
        };
        let layout = self.pipelines.texture_layout.clone();
        for resolved in sources {
            let passes: Vec<usize> = if request.all_variants {
                (0..resolved.all.len()).collect()
            } else {
                request.passes.clone()
            };
            for index in passes {
                let pass = match resolved
                    .all
                    .get(index)
                    .ok_or("warm-up pass is out of range")?
                {
                    Ok(pass) => pass,
                    Err(why) => {
                        tracing::warn!(
                            "shader `{}` pass {index} cannot draw during warm-up: {why}",
                            resolved.shader
                        );
                        continue;
                    }
                };
                if pass.grab.is_some() {
                    continue;
                }
                let variants: Box<dyn Iterator<Item = Vec<String>>> = if request.all_variants {
                    Box::new(
                        self.compiled
                            .warmup_variants(&pass.program, self.color_space == 1)?,
                    )
                } else {
                    Box::new(std::iter::once(material.keywords.clone()))
                };
                for variant in variants {
                    // The variants draws take ([`Executor::plan_draw`]): the draw decides
                    // instancing, so a variant's INSTANCING_ON is left out, and an instanced
                    // mesh call draws the replay variant.
                    let instancing = variant.iter().any(|k| k == super::INSTANCING_KEYWORD);
                    for replay in [false, true] {
                        if replay && !instancing {
                            continue;
                        }
                        let mut keywords: Vec<String> = variant
                            .iter()
                            .filter(|k| *k != super::INSTANCING_KEYWORD)
                            .cloned()
                            .collect();
                        if replay {
                            keywords.push(super::INSTANCING_KEYWORD.into());
                            keywords.push(unbound_shaders::MESH_INSTANCE_REPLAY.into());
                        }
                        let (id, errored) = self.variant_or_error(
                            &resolved.shader,
                            &pass.program,
                            &keywords,
                            self.color_space == 1,
                            &layout,
                        );
                        let id = id.ok_or_else(|| {
                            format!(
                                "shader `{}` pass {index} could not be warmed up",
                                resolved.shader
                            )
                        })?;
                        // Program preparation does not require this attachment configuration.
                        // As with other later target changes, an incompatible scalar type can
                        // require a pipeline on the first draw into the appropriate target.
                        if !self
                            .compiled
                            .accepts_target(id, target.format, &target.extra_formats())
                        {
                            continue;
                        }
                        for topology in [
                            resources::Topology::Triangles,
                            resources::Topology::Lines,
                            resources::Topology::LineStrip,
                            resources::Topology::Points,
                        ] {
                            for mirrored in [false, true] {
                                let (state, _) = self.draw_state(
                                    if errored {
                                        Default::default()
                                    } else {
                                        pass.state
                                    },
                                    pass.stencil_ref,
                                    mirrored,
                                    self.conventions.top_down(target.screen_like),
                                );
                                self.compiled.pipeline(
                                    &self.device,
                                    id,
                                    PipelineKey {
                                        state,
                                        topology,
                                        format: target.format,
                                        depth: target.depth_format,
                                        samples: target.samples,
                                        depth_bias: self.state.depth_bias.map(f32::to_bits),
                                        extra: target.extra_formats(),
                                    },
                                )?;
                            }
                        }
                    }
                }
            }
        }
        Ok("{}".into())
    }

    /// A pass's state as a draw uses it: `mirrored` (a matrix of negative determinant, a
    /// mirrored instance flag), `GL.invertCulling` and a target stored the other way up
    /// (`target_flipped`) each flip which faces are culled. The stencil's front and back states
    /// follow the faces as the device sees them: a mirrored draw's turned faces take the back
    /// state, while inverted culling turns which faces count as front, as the target's flip does
    /// (the Unity 6000.0.71f1 Editor on OpenGL Core and Vulkan, tests/projects/samplerstates
    /// `StencilFaces.*`: plain and inverted culling, each with and without a mirrored matrix).
    pub(super) fn draw_state(
        &self,
        mut state: crate::materials::RenderState,
        mut stencil_ref: u32,
        mirrored: bool,
        target_flipped: bool,
    ) -> (crate::materials::RenderState, u32) {
        if state.gui_ztest {
            state.ztest = if self.state.overlay { 8 } else { 4 };
        }
        if let Some(o) = &self.state.state_override {
            stencil_ref = apply_override(&mut state, stencil_ref, o);
        }
        state.wireframe = self.state.wireframe;
        // The device's depth direction: tests and offsets mirrored on reversed Z.
        if self.conventions.reversed_z {
            state.ztest = super::mirror_compare(state.ztest);
            state.offset = state.offset.map(|o| -o);
        }
        if self.state.invert_culling != (mirrored != target_flipped) {
            state.cull = match state.cull {
                1 => 2,
                2 => 1,
                c => c,
            };
        }
        if self.state.invert_culling != target_flipped {
            // The faces count the other way round: so do their stencil settings.
            state.stencil = state.stencil.map(crate::materials::StencilState::swapped);
        }
        (state, stencil_ref)
    }
}
